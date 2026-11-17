using SteamSync.Worker.Infrastructure.Steam.Models;

namespace SteamSync.Worker.Application.Steam.Interfaces;
public interface ISteamSyncService
{
    Task SyncLibraryAsync(
        int userId,
        string steamId,
        LibrarySyncScope scope = LibrarySyncScope.Full,
        int maxStoreEnrich = 5,
        CancellationToken cancellationToken = default);

    Task SyncGameAchievementsAsync(
        int userId,
        string steamId,
        int appId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists achievement catalog/unlocks using Steam payloads fetched outside a DB transaction.
    /// </summary>
    Task SyncGameAchievementsAsync(
        int userId,
        string steamId,
        int appId,
        GameSchema? prefetchedSchema,
        PlayerAchievementsResult? prefetchedPlayer,
        CancellationToken cancellationToken = default);

    /// <returns>True when Steam returned usable achievement data for the game.</returns>
    Task<bool> SyncGameCompletionPercentageAsync(
        int userId,
        string steamId,
        int appId,
        CancellationToken cancellationToken = default);

    Task<int> CountEligibleCrawlGamesAsync(int userId, CancellationToken cancellationToken = default);

    Task SyncAchievementsForUserAsync(
        int userId,
        string steamId,
        AchievementSyncScope scope,
        CancellationToken cancellationToken = default);

    Task RecomputeUserAchievementStatsAsync(int userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<int>> GetPriorityCompletionAppIdsAsync(
        int userId,
        string steamId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<int>> GetCrawlCompletionAppIdsAsync(
        int userId,
        int skip,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Recent two-week app IDs for the user that have community stats and are not marked unavailable.
    /// </summary>
    Task<IReadOnlyList<int>> GetRecentAchievementSyncAppIdsAsync(
        int userId,
        string steamId,
        CancellationToken cancellationToken = default);
}
