using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SteamSync.Shared.Messages;
using SteamSync.Worker.Infrastructure.Persistence;
using SteamSync.Worker.Infrastructure.Metrics;
using SteamSync.Worker.Infrastructure.Options;
using SteamSync.Worker.Application.Steam;
using SteamSync.Worker.Application.Steam.Interfaces;
using SteamSync.Worker.Infrastructure.Steam;

namespace SteamSync.Worker.Application.Steam;

/// <summary>Handles <see cref="UserSyncJob"/> messages from RabbitMQ.</summary>
public class UserSyncMessageHandler
{
    private readonly ISteamSyncService _steamSyncService;
    private readonly ISyncRepository _syncRepository;
    private readonly WorkerDbContext _db;
    private readonly SteamApiThrottle _steamGate;
    private readonly SyncWorkerOptions _options;
    private readonly ILogger<UserSyncMessageHandler> _logger;

    public UserSyncMessageHandler(
        ISteamSyncService steamSyncService,
        ISyncRepository syncRepository,
        WorkerDbContext db,
        SteamApiThrottle steamGate,
        IOptions<SyncWorkerOptions> options,
        ILogger<UserSyncMessageHandler> logger)
    {
        _steamSyncService = steamSyncService;
        _syncRepository = syncRepository;
        _db = db;
        _steamGate = steamGate;
        _options = options.Value;
        _logger = logger;
    }

    public async Task HandleAsync(UserSyncJob job, CancellationToken cancellationToken = default)
    {
        var payload = job.Payload
            ?? throw new InvalidOperationException("UserSyncJob.Payload is required.");

        var userId = payload.UserId;
        var steamId = payload.SteamId;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        _logger.LogInformation(
            "Job started {JobId} type={JobType} user={UserId} steam={SteamId}",
            job.JobId,
            job.JobType,
            userId,
            steamId);

        try
        {
            await _steamGate.WaitAsync(cancellationToken);
            try
            {
                var ownedCount = await _db.UsersGames.AsNoTracking()
                    .CountAsync(ug => ug.UserId == userId, cancellationToken);
                await _syncRepository.MarkSyncingAsync(userId, job.JobId, ownedCount, cancellationToken);

                var wasFull = false;
                switch (job.JobType)
                {
                    case SyncJobTypes.FullLibraryResync:
                        wasFull = true;
                        await RunFullLibraryResyncAsync(userId, steamId, cancellationToken);
                        break;
                    case SyncJobTypes.RecentActivityOnly:
                        await RunRecentActivityAsync(userId, steamId, payload.IncludeCrawl, cancellationToken);
                        break;
                    case SyncJobTypes.UserSync:
                        if (payload.AppId is int appId)
                        {
                            await _steamSyncService.SyncGameAchievementsAsync(userId, steamId, appId, cancellationToken);
                        }
                        else
                        {
                            await RunRecentActivityAsync(userId, steamId, payload.IncludeCrawl, cancellationToken);
                        }
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown JobType '{job.JobType}'");
                }

                var synced = await _db.UsersGames.AsNoTracking()
                    .CountAsync(
                        ug => ug.UserId == userId
                            && ug.Game.HasCommunityVisibleStats == true
                            && ug.AchievementsSyncedAt != null
                            && !ug.AchievementSyncUnavailable,
                        cancellationToken);
                var total = await _db.UsersGames.AsNoTracking()
                    .CountAsync(ug => ug.UserId == userId && ug.Game.HasCommunityVisibleStats == true, cancellationToken);

                await _syncRepository.MarkCompleteAsync(userId, wasFull, synced, total, cancellationToken);

                if (wasFull)
                {
                    await _syncRepository.TryActivateProvisioningUserAsync(userId, cancellationToken);
                }

                SyncMetrics.JobsProcessedTotal.Add(1);
                _logger.LogInformation(
                    "Job completed {JobId} user={UserId} progress=100% synced={Synced}/{Total}",
                    job.JobId,
                    userId,
                    synced,
                    total);
            }
            finally
            {
                _steamGate.Release();
            }
        }
        catch (Exception ex)
        {
            SyncMetrics.JobsFailedTotal.Add(1);
            _logger.LogError(ex, "Job failed {JobId} user={UserId}", job.JobId, userId);
            await _syncRepository.MarkFailedAsync(userId, ex.Message, cancellationToken);
            throw;
        }
        finally
        {
            sw.Stop();
            SyncMetrics.SyncDurationSeconds.Record(sw.Elapsed.TotalSeconds);
        }
    }

    private async Task RunFullLibraryResyncAsync(int userId, string steamId, CancellationToken ct)
    {
        await _steamSyncService.SyncLibraryAsync(
            userId,
            steamId,
            LibrarySyncScope.Full,
            _options.MaxStoreEnrichPerLibrarySync,
            ct);

        await RunPriorityAsync(userId, steamId, ct);
        await RunCrawlToCompletionAsync(userId, steamId, ct);
    }

    private async Task RunRecentActivityAsync(int userId, string steamId, bool includeCrawl, CancellationToken ct)
    {
        await _steamSyncService.SyncLibraryAsync(
            userId,
            steamId,
            LibrarySyncScope.Recent,
            _options.MaxStoreEnrichPerLibrarySync,
            ct);

        await RunPriorityAsync(userId, steamId, ct);

        if (includeCrawl)
        {
            await RunCrawlToCompletionAsync(userId, steamId, ct);
        }
    }

    private async Task RunPriorityAsync(int userId, string steamId, CancellationToken ct)
    {
        var appIds = await _steamSyncService.GetPriorityCompletionAppIdsAsync(userId, steamId, ct);
        var total = appIds.Count;
        var done = 0;
        foreach (var appId in appIds)
        {
            await _steamSyncService.SyncGameCompletionPercentageAsync(userId, steamId, appId, ct);
            done++;
            var pct = total == 0 ? 100m : Math.Round((decimal)done / total * 50, 2); // priority = first half
            await _syncRepository.UpdateProgressAsync(userId, done, total, pct, ct);
            _logger.LogDebug("Priority progress user={UserId} {Done}/{Total} ({Pct}%)", userId, done, total, pct);
        }
    }

    private async Task RunCrawlToCompletionAsync(int userId, string steamId, CancellationToken ct)
    {
        var maxBatches = 500;
        for (var batch = 0; batch < maxBatches; batch++)
        {
            var appIds = await _steamSyncService.GetCrawlCompletionAppIdsAsync(
                userId,
                0,
                _options.BatchSize,
                ct);

            if (appIds.Count == 0)
            {
                break;
            }

            var success = 0;
            foreach (var appId in appIds)
            {
                if (await _steamSyncService.SyncGameCompletionPercentageAsync(userId, steamId, appId, ct))
                {
                    success++;
                }
            }

            var remaining = await _steamSyncService.CountEligibleCrawlGamesAsync(userId, ct);
            var synced = await _db.UsersGames.AsNoTracking()
                .CountAsync(
                    ug => ug.UserId == userId
                        && ug.Game.HasCommunityVisibleStats == true
                        && ug.AchievementsSyncedAt != null
                        && !ug.AchievementSyncUnavailable,
                    ct);
            var total = await _db.UsersGames.AsNoTracking()
                .CountAsync(ug => ug.UserId == userId && ug.Game.HasCommunityVisibleStats == true, ct);
            var pct = total == 0 ? 100m : Math.Round((decimal)synced / total * 100, 2);
            await _syncRepository.UpdateProgressAsync(userId, synced, total, pct, ct);

            _logger.LogInformation(
                "Crawl batch {Batch} user={UserId} success={Success} remaining={Remaining} progress={Pct}%",
                batch,
                userId,
                success,
                remaining,
                pct);

            if (remaining <= 0 || (success == 0 && appIds.Count < _options.BatchSize))
            {
                break;
            }
        }
    }
}
