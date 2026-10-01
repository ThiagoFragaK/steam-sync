using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SteamSync.Shared;
using SteamSync.Shared.Messages;
using SteamSync.Worker.Application.Steam.Interfaces;
using SteamSync.Worker.Infrastructure.Options;
using SteamSync.Worker.Infrastructure.Persistence;

namespace SteamSync.Worker.Application.Steam;

public class SyncLibraryHandler
{
    private readonly ISteamSyncService _steamSyncService;
    private readonly ISyncRepository _syncRepository;
    private readonly ISyncJobPublisher _publisher;
    private readonly WorkerDbContext _db;
    private readonly SyncWorkerOptions _options;
    private readonly ILogger<SyncLibraryHandler> _logger;

    public SyncLibraryHandler(
        ISteamSyncService steamSyncService,
        ISyncRepository syncRepository,
        ISyncJobPublisher publisher,
        WorkerDbContext db,
        IOptions<SyncWorkerOptions> options,
        ILogger<SyncLibraryHandler> logger)
    {
        _steamSyncService = steamSyncService;
        _syncRepository = syncRepository;
        _publisher = publisher;
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    public async Task HandleAsync(SyncLibraryJob job, CancellationToken cancellationToken = default)
    {
        await _syncRepository.SetPipelineStageAsync(job.UserId, PipelineStage.FullLibrary, cancellationToken);

        try
        {
            await _steamSyncService.SyncLibraryAsync(
                job.UserId,
                job.SteamId,
                LibrarySyncScope.Full,
                maxStoreEnrich: _options.MaxStoreEnrichPerLibrarySync,
                cancellationToken);

            var steamAppIds = await _db.UsersGames
                .AsNoTracking()
                .Where(ug => ug.UserId == job.UserId
                    && ug.Game.HasCommunityVisibleStats == true
                    && !ug.AchievementSyncUnavailable
                    && (ug.AchievementsSyncedAt == null || ug.NeedsAchievementRefresh)
                    && ug.Game.GameSteamId != null)
                .Select(ug => ug.Game.GameSteamId)
                .ToListAsync(cancellationToken);

            var appIds = SyncPipelineHelper.ParseSteamAppIds(steamAppIds)
                .Distinct()
                .ToList();

            if (appIds.Count == 0)
            {
                await _syncRepository.ActivateUserAsync(job.UserId, cancellationToken);
                await _syncRepository.MarkCompleteAsync(
                    job.UserId,
                    wasFullSync: true,
                    gamesSynced: 0,
                    totalGames: 0,
                    cancellationToken);

                _logger.LogInformation(
                    "SyncLibrary for user {UserId}: no remaining achievements; user Active",
                    job.UserId);
                return;
            }

            await _syncRepository.StartPipelineWaveAsync(
                job.UserId,
                job.JobId,
                PipelineStage.FullAchievements,
                appIds.Count,
                cancellationToken);

            foreach (var chunk in SyncPipelineHelper.ChunkDistinctAppIds(appIds))
            {
                await _publisher.PublishGamesListSyncAsync(
                    new GamesListSyncJob
                    {
                        JobId = Guid.NewGuid(),
                        UserId = job.UserId,
                        SteamId = job.SteamId,
                        AppIds = chunk,
                        Priority = SyncJobPriorities.Low
                    },
                    cancellationToken);
            }

            _logger.LogInformation(
                "SyncLibrary for user {UserId}: fanned out {Count} remaining achievement jobs",
                job.UserId,
                appIds.Count);
        }
        catch (Exception ex)
        {
            await _syncRepository.MarkFailedAsync(job.UserId, ex.Message, cancellationToken);
            throw;
        }
    }
}
