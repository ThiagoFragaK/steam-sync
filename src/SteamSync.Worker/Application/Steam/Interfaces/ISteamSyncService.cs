using SteamSync.Worker.Application.Steam;
using SteamSync.Worker.Application.Steam.Interfaces;
using SteamSync.Worker.Infrastructure.Steam;

namespace SteamSync.Worker.Application.Steam.Interfaces;
public interface ISteamSyncService
{
    Task SyncLibraryAsync(
        int userId,
        string steamId,
        LibrarySyncScope scope = LibrarySyncScope.Full,
        int maxStoreEnrich = 5,
        CancellationToken cancellationToken = default);

    Task SyncGameAchievementsAsync(int userId, string steamId, int appId, CancellationToken cancellationToken = default);

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
}
