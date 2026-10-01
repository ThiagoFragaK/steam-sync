using Microsoft.EntityFrameworkCore;
using SteamSync.Worker.Application.Steam.Interfaces;
using SteamSync.Worker.Domain.Enums;
using SteamSync.Worker.Infrastructure.Persistence;

namespace SteamSync.Worker.Application.Steam;

public class FirstSyncHandler
{
    private readonly ISteamSyncService _steamSyncService;
    private readonly ISyncRepository _syncRepository;
    private readonly WorkerDbContext _db;
    private readonly ILogger<FirstSyncHandler> _logger;

    public FirstSyncHandler(
        ISteamSyncService steamSyncService,
        ISyncRepository syncRepository,
        WorkerDbContext db,
        ILogger<FirstSyncHandler> logger)
    {
        _steamSyncService = steamSyncService;
        _syncRepository = syncRepository;
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

            var gameCount = await _db.UsersGames.CountAsync(ug => ug.UserId == userId, cancellationToken);

            // Phase 1 interim: promote to Active so register UI can finish.
            // Phase 2 will keep Syncing until GamesListSync / achievements complete.
            if (user.Status == (int)StatusEnum.FirstSync)
            {
                user.Status = (int)StatusEnum.Active;
                await _db.SaveChangesAsync(cancellationToken);
            }

            await _syncRepository.MarkCompleteAsync(
                userId,
                wasFullSync: false,
                gamesSynced: gameCount,
                totalGames: gameCount,
                cancellationToken);

            _logger.LogInformation(
                "FirstSync completed for user {UserId}: {GameCount} recent games",
                userId,
                gameCount);
        }
        catch (Exception ex)
        {
            await _syncRepository.MarkFailedAsync(userId, ex.Message, cancellationToken);
            throw;
        }
    }
}
