using Microsoft.EntityFrameworkCore;
using SteamSync.Shared;
using SteamSync.Shared.Messages;
using SteamSync.Worker.Application.Steam.Interfaces;
using SteamSync.Worker.Domain.Enums;
using SteamSync.Worker.Infrastructure.Persistence;

namespace SteamSync.Worker.Application.Steam;

public class FirstSyncHandler
{
    private readonly ISteamSyncService _steamSyncService;
    private readonly ISyncRepository _syncRepository;
    private readonly ISyncJobPublisher _publisher;
    private readonly WorkerDbContext _db;
    private readonly ILogger<FirstSyncHandler> _logger;

    public FirstSyncHandler(
        ISteamSyncService steamSyncService,
        ISyncRepository syncRepository,
        ISyncJobPublisher publisher,
        WorkerDbContext db,
        ILogger<FirstSyncHandler> logger)
    {
        _steamSyncService = steamSyncService;
        _syncRepository = syncRepository;
        _publisher = publisher;
        _db = db;
        _logger = logger;
    }

    public async Task HandleAsync(int userId, string steamId, Guid jobId, CancellationToken cancellationToken = default)
    {
        await _syncRepository.UpsertPendingAsync(userId, jobId, cancellationToken);
        await _syncRepository.MarkSyncingAsync(userId, jobId, totalGames: 0, cancellationToken);

        try
        {
            await _steamSyncService.SyncLibraryAsync(
                userId,
                steamId,
                LibrarySyncScope.Recent,
                maxStoreEnrich: 0,
                cancellationToken);

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                ?? throw new InvalidOperationException($"User {userId} was not found.");

            if (user.Status is (int)StatusEnum.FirstSync or (int)StatusEnum.Active)
            {
                user.Status = (int)StatusEnum.Syncing;
                await _db.SaveChangesAsync(cancellationToken);
            }

            var steamAppIds = await _db.UsersGames
                .AsNoTracking()
                .Where(ug => ug.UserId == userId && ug.Game.GameSteamId != null)
                .Select(ug => ug.Game.GameSteamId)
                .ToListAsync(cancellationToken);

            var appIds = SyncPipelineHelper.ParseSteamAppIds(steamAppIds);

            if (appIds.Count == 0)
            {
                await _syncRepository.SetPipelineStageAsync(userId, PipelineStage.FullLibrary, cancellationToken);
                await _syncRepository.ActivateUserAsync(userId, cancellationToken);
                await _publisher.PublishSyncLibraryAsync(
                    new SyncLibraryJob
                    {
                        JobId = Guid.NewGuid(),
                        UserId = userId,
                        SteamId = steamId
                    },
                    cancellationToken);

                _logger.LogInformation(
                    "FirstSync for user {UserId}: no recent games; user Active, published SyncLibrary",
                    userId);
                return;
            }

            await _syncRepository.StartPipelineWaveAsync(
                userId,
                jobId,
                PipelineStage.RecentAchievements,
                appIds.Count,
                cancellationToken);

            foreach (var chunk in SyncPipelineHelper.ChunkDistinctAppIds(appIds))
            {
                await _publisher.PublishGamesListSyncAsync(
                    new GamesListSyncJob
                    {
                        JobId = Guid.NewGuid(),
                        UserId = userId,
                        SteamId = steamId,
                        AppIds = chunk,
                        Priority = SyncJobPriorities.High
                    },
                    cancellationToken);
            }

            _logger.LogInformation(
                "FirstSync for user {UserId}: {GameCount} recent games; published GamesListSync chunks",
                userId,
                appIds.Count);
        }
        catch (Exception ex)
        {
            await _syncRepository.MarkFailedAsync(userId, ex.Message, cancellationToken);
            throw;
        }
    }
}
