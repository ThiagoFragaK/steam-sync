using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SteamSync.Shared;
using SteamSync.Shared.Messages;
using SteamSync.Worker.Data;
using SteamSync.Worker.Metrics;
using SteamSync.Worker.Options;
using SteamSync.Worker.Repositories;
using SteamSync.Worker.Services;
using SteamSync.Worker.Services.Interfaces;

namespace SteamSync.Worker.MessageHandlers;

/// <summary>Consumes <see cref="UserSyncJob"/> messages from RabbitMQ.</summary>
public class UserSyncMessageHandler : IConsumer<UserSyncJob>
{
    private const int InitialPriorityCap = 50;

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

    public async Task Consume(ConsumeContext<UserSyncJob> context)
    {
        var job = context.Message;
        var payload = job.Payload
            ?? throw new InvalidOperationException("UserSyncJob.Payload is required.");

        var userId = payload.UserId;
        var steamId = payload.SteamId;
        var ct = context.CancellationToken;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        _logger.LogInformation(
            "Job started {JobId} type={JobType} scope={Scope} user={UserId} steam={SteamId}",
            job.JobId,
            job.JobType,
            payload.Scope,
            userId,
            steamId);
        // #region agent log
        try { System.IO.File.AppendAllText(@"K:\Projekten\MyApps\achiev-hub\debug-321fb6.log", System.Text.Json.JsonSerializer.Serialize(new { sessionId = "321fb6", runId = "pre-fix", hypothesisId = "B", location = "UserSyncMessageHandler.cs:Consume:start", message = "Worker consumed UserSyncJob", data = new { jobId = job.JobId, job.JobType, payload.Scope, userId, hasSteamId = !string.IsNullOrWhiteSpace(steamId), payload.AppId, payload.IncludeCrawl }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }) + "\n"); } catch { }
        // #endregion

        try
        {
            var existing = await _syncRepository.GetAsync(userId, ct);
            if (existing is not null
                && existing.LastJobId == job.JobId
                && (existing.Status == SyncStatus.Complete
                    || existing.Status == SyncStatus.Partial
                    || existing.Status == SyncStatus.Failed))
            {
                _logger.LogInformation(
                    "Skipping already-terminal job {JobId} user={UserId} status={Status}",
                    job.JobId,
                    userId,
                    existing.Status);
                // #region agent log
                try { System.IO.File.AppendAllText(@"K:\Projekten\MyApps\achiev-hub\debug-321fb6.log", System.Text.Json.JsonSerializer.Serialize(new { sessionId = "321fb6", runId = "pre-fix", hypothesisId = "E", location = "UserSyncMessageHandler.cs:Consume:skip-terminal", message = "Worker skipped already-terminal job", data = new { jobId = job.JobId, userId, status = existing.Status.ToString() }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }) + "\n"); } catch { }
                // #endregion
                return;
            }

            await _steamGate.WaitAsync(ct);
            try
            {
                var ownedCount = await _db.UsersGames.AsNoTracking()
                    .CountAsync(ug => ug.UserId == userId, ct);
                await _syncRepository.MarkSyncingAsync(userId, job.JobId, ownedCount, ct);

                var wasFull = false;
                var wasInitial = false;
                switch (job.JobType)
                {
                    case SyncJobTypes.FullLibraryResync:
                        wasFull = true;
                        await RunFullLibraryResyncAsync(userId, steamId, ct);
                        break;
                    case SyncJobTypes.RecentActivityOnly:
                        await RunRecentActivityAsync(userId, steamId, payload.IncludeCrawl, ct);
                        break;
                    case SyncJobTypes.UserSync:
                        if (payload.AppId is int appId)
                        {
                            await _steamSyncService.SyncGameAchievementsAsync(userId, steamId, appId, ct);
                        }
                        else if (string.Equals(payload.Scope, SyncJobScopes.Initial, StringComparison.OrdinalIgnoreCase))
                        {
                            wasInitial = true;
                            await RunInitialSyncAsync(userId, steamId, ct);
                        }
                        else
                        {
                            await RunRecentActivityAsync(userId, steamId, payload.IncludeCrawl, ct);
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
                            && !ug.NeedsAchievementRefresh
                            && !ug.AchievementSyncUnavailable,
                        ct);
                var total = await _db.UsersGames.AsNoTracking()
                    .CountAsync(ug => ug.UserId == userId && ug.Game.HasCommunityVisibleStats == true, ct);

                if (wasInitial)
                {
                    await _syncRepository.MarkPartialAsync(userId, synced, total, ct);
                    await _syncRepository.TryActivateProvisioningUserAsync(userId, ct);

                    // Initial only covers a priority cap; chain a full crawl so Partial does not stall.
                    var followUp = new UserSyncJob
                    {
                        JobType = SyncJobTypes.FullLibraryResync,
                        Payload = new UserSyncPayload
                        {
                            UserId = userId,
                            SteamId = steamId,
                            Priority = "low"
                        }
                    };
                    await _syncRepository.UpsertPendingAsync(userId, followUp.JobId, ct);
                    var endpoint = await context.GetSendEndpoint(new Uri($"queue:{SyncQueueNames.Jobs}"));
                    await endpoint.Send(followUp, ct);
                    // #region agent log
                    try { System.IO.File.AppendAllText(@"K:\Projekten\MyApps\achiev-hub\debug-321fb6.log", System.Text.Json.JsonSerializer.Serialize(new { sessionId = "321fb6", runId = "post-fix", hypothesisId = "E", location = "UserSyncMessageHandler.cs:Consume:chain-full", message = "Chained FullLibraryResync after initial", data = new { userId, followUpJobId = followUp.JobId, synced, total }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }) + "\n"); } catch { }
                    // #endregion
                }
                else if (wasFull)
                {
                    await _syncRepository.MarkCompleteAsync(userId, wasFullSync: true, synced, total, ct);
                    await _syncRepository.TryActivateProvisioningUserAsync(userId, ct);
                }
                else
                {
                    // Recent / single-game: keep Partial if never fully synced.
                    var prior = await _syncRepository.GetAsync(userId, ct);
                    if (prior?.LastFullSync is null)
                    {
                        await _syncRepository.MarkPartialAsync(userId, synced, total, ct);
                    }
                    else
                    {
                        await _syncRepository.MarkCompleteAsync(userId, wasFullSync: false, synced, total, ct);
                    }
                }

                SyncMetrics.JobsProcessedTotal.Add(1);
                _logger.LogInformation(
                    "Job completed {JobId} user={UserId} synced={Synced}/{Total} initial={Initial} full={Full}",
                    job.JobId,
                    userId,
                    synced,
                    total,
                    wasInitial,
                    wasFull);
                // #region agent log
                try { System.IO.File.AppendAllText(@"K:\Projekten\MyApps\achiev-hub\debug-321fb6.log", System.Text.Json.JsonSerializer.Serialize(new { sessionId = "321fb6", runId = "pre-fix", hypothesisId = "D", location = "UserSyncMessageHandler.cs:Consume:completed", message = "Worker job completed", data = new { jobId = job.JobId, userId, synced, total, wasInitial, wasFull, job.JobType }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }) + "\n"); } catch { }
                // #endregion
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
            // #region agent log
            try { System.IO.File.AppendAllText(@"K:\Projekten\MyApps\achiev-hub\debug-321fb6.log", System.Text.Json.JsonSerializer.Serialize(new { sessionId = "321fb6", runId = "pre-fix", hypothesisId = "E", location = "UserSyncMessageHandler.cs:Consume:failed", message = "Worker job failed", data = new { jobId = job.JobId, userId, error = ex.Message }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }) + "\n"); } catch { }
            // #endregion
            await _syncRepository.MarkFailedAsync(userId, ex.Message, ct);
            throw; // MassTransit retry / DLX
        }
        finally
        {
            sw.Stop();
            SyncMetrics.SyncDurationSeconds.Record(sw.Elapsed.TotalSeconds);
        }
    }

    private async Task RunInitialSyncAsync(int userId, string steamId, CancellationToken ct)
    {
        await _steamSyncService.SyncLibraryAsync(
            userId,
            steamId,
            LibrarySyncScope.Full,
            _options.MaxStoreEnrichPerLibrarySync,
            ct);

        var appIds = await _steamSyncService.GetPriorityCompletionAppIdsAsync(userId, steamId, ct);
        if (appIds.Count > InitialPriorityCap)
        {
            appIds = appIds.Take(InitialPriorityCap).ToList();
        }

        var total = appIds.Count;
        var done = 0;
        foreach (var appId in appIds)
        {
            await _steamSyncService.SyncGameAchievementsAsync(userId, steamId, appId, ct);
            done++;
            var pct = total == 0 ? 100m : Math.Round((decimal)done / total * 100, 2);
            await _syncRepository.UpdateProgressAsync(userId, done, total, pct, ct);
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

        // Prior completion-% sync marked AchievementsSyncedAt without writing unlock rows.
        // Re-queue games that look %-only (have % > 0 but zero unlock rows, or never got schema).
        var marked = await _db.UsersGames
            .Where(ug => ug.UserId == userId
                && ug.Game.HasCommunityVisibleStats == true
                && !ug.AchievementSyncUnavailable
                && ug.AchievementsSyncedAt != null
                && !ug.NeedsAchievementRefresh
                && (
                    (ug.AchievementsPercentage > 0
                        && !_db.UsersAchievements.Any(ua => ua.UserId == ug.UserId && ua.GameId == ug.GameId))
                    || ug.Game.SchemaSyncedAt == null))
            .ExecuteUpdateAsync(
                s => s.SetProperty(ug => ug.NeedsAchievementRefresh, true),
                ct);
        // #region agent log
        try { System.IO.File.AppendAllText(@"K:\Projekten\MyApps\achiev-hub\debug-321fb6.log", System.Text.Json.JsonSerializer.Serialize(new { sessionId = "321fb6", runId = "post-fix", hypothesisId = "G", location = "UserSyncMessageHandler.cs:RunFullLibraryResync:backfill-flag", message = "Marked NeedsAchievementRefresh for %-only games", data = new { userId, marked }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }) + "\n"); } catch { }
        // #endregion

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
            await _steamSyncService.SyncGameAchievementsAsync(userId, steamId, appId, ct);
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
                if (await _steamSyncService.SyncGameAchievementsAsync(userId, steamId, appId, ct))
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
                        && !ug.NeedsAchievementRefresh
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
