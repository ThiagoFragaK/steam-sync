using Microsoft.EntityFrameworkCore;
using SteamSync.Shared;
using SteamSync.Shared.Messages;
using SteamSync.Worker.Application.Steam.Interfaces;
using SteamSync.Worker.Domain.Entities;
using SteamSync.Worker.Infrastructure.Persistence;

namespace SteamSync.Worker.Application.Steam;

public class UserAchievementsSyncHandler
{
    private readonly ISteamSyncService _steamSyncService;
    private readonly ISyncRepository _syncRepository;
    private readonly ISyncJobPublisher _publisher;
    private readonly WorkerDbContext _db;
    private readonly ILogger<UserAchievementsSyncHandler> _logger;

    public UserAchievementsSyncHandler(
        ISteamSyncService steamSyncService,
        ISyncRepository syncRepository,
        ISyncJobPublisher publisher,
        WorkerDbContext db,
        ILogger<UserAchievementsSyncHandler> logger)
    {
        _steamSyncService = steamSyncService;
        _syncRepository = syncRepository;
        _publisher = publisher;
        _db = db;
        _logger = logger;
    }

    public async Task HandleAsync(UserAchievementsSyncJob job, CancellationToken cancellationToken = default)
    {
        var shouldCount = true;
        try
        {
            shouldCount = await SyncWithLockAsync(job.UserId, job.SteamId, job.AppId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "UserAchievementsSync failed for user {UserId} app {AppId}; counting wave progress after retries",
                job.UserId,
                job.AppId);
            throw;
        }

        if (shouldCount)
        {
            await AfterGameHandledAsync(job.UserId, job.SteamId, cancellationToken);
        }
    }

    /// <summary>Counts progress after a terminal failure (retries exhausted).</summary>
    public Task CountFailedAttemptAsync(int userId, string steamId, CancellationToken cancellationToken = default) =>
        AfterGameHandledAsync(userId, steamId, cancellationToken);

    /// <returns>True when this delivery should increment wave progress.</returns>
    private async Task<bool> SyncWithLockAsync(
        int userId,
        string steamId,
        int appId,
        CancellationToken cancellationToken)
    {
        if (appId <= 0)
        {
            return true;
        }

        var steamAppId = appId.ToString();
        var game = await _db.Games
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.GameSteamId == steamAppId, cancellationToken);

        if (game is null)
        {
            _logger.LogDebug("UserAchievementsSync skip app {AppId}: game missing", appId);
            return true;
        }

        var rowExists = await _db.UsersGames
            .AsNoTracking()
            .AnyAsync(ug => ug.UserId == userId && ug.GameId == game.Id, cancellationToken);

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var usersGame = await _db.UsersGames
                .FromSqlInterpolated($@"
                    SELECT * FROM users_games
                    WHERE ""UserId"" = {userId} AND ""GameId"" = {game.Id}
                    FOR UPDATE SKIP LOCKED")
                .FirstOrDefaultAsync(cancellationToken);

            if (usersGame is null)
            {
                await tx.CommitAsync(cancellationToken);
                if (rowExists)
                {
                    // Another worker holds the lock — they own wave progress for this game.
                    _logger.LogDebug(
                        "UserAchievementsSync skip app {AppId} user {UserId}: locked by another worker",
                        appId,
                        userId);
                    return false;
                }

                _logger.LogDebug(
                    "UserAchievementsSync skip app {AppId} user {UserId}: missing UsersGame row",
                    appId,
                    userId);
                return true;
            }

            if (IsAlreadySynced(usersGame))
            {
                _logger.LogDebug(
                    "UserAchievementsSync skip app {AppId} user {UserId}: already synced",
                    appId,
                    userId);
                await tx.CommitAsync(cancellationToken);
                return true;
            }

            await _steamSyncService.SyncGameAchievementsAsync(userId, steamId, appId, cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task AfterGameHandledAsync(int userId, string steamId, CancellationToken cancellationToken)
    {
        var progress = await _syncRepository.IncrementWaveProgressAsync(userId, cancellationToken);
        if (progress is null)
        {
            return;
        }

        if (progress.GamesSyncedCount < progress.TotalGamesCount)
        {
            return;
        }

        if (progress.PipelineStage == PipelineStage.RecentAchievements)
        {
            var advanced = await _syncRepository.TryAdvancePipelineStageAsync(
                userId,
                PipelineStage.RecentAchievements,
                PipelineStage.FullLibrary,
                cancellationToken);

            if (!advanced)
            {
                return;
            }

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
                "Recent achievements wave complete for user {UserId}; user Active, published SyncLibrary",
                userId);
            return;
        }

        if (progress.PipelineStage == PipelineStage.FullAchievements)
        {
            var advanced = await _syncRepository.TryAdvancePipelineStageAsync(
                userId,
                PipelineStage.FullAchievements,
                PipelineStage.Done,
                cancellationToken);

            if (!advanced)
            {
                return;
            }

            await _syncRepository.ActivateUserAsync(userId, cancellationToken);
            await _syncRepository.MarkCompleteAsync(
                userId,
                wasFullSync: true,
                gamesSynced: progress.GamesSyncedCount,
                totalGames: progress.TotalGamesCount,
                cancellationToken);

            _logger.LogInformation(
                "Full achievements wave complete for user {UserId}; user Active",
                userId);
        }
    }

    private static bool IsAlreadySynced(UsersGame usersGame) =>
        usersGame.AchievementSyncUnavailable
        || (usersGame.AchievementsSyncedAt is not null && !usersGame.NeedsAchievementRefresh);
}
