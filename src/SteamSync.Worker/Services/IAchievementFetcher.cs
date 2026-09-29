using SteamSync.Worker.Models;

namespace SteamSync.Worker.Services;

public interface IAchievementFetcher
{
    Task<Player?> GetPlayerBySteamIdAsync(string steamId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RecentlyPlayedGame>> GetRecentlyPlayedGamesAsync(string steamId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OwnedGame>> GetOwnedGamesAsync(string steamId, CancellationToken cancellationToken = default);
    Task<PlayerAchievementsResult?> GetPlayerAchievementsAsync(string steamId, int appId, CancellationToken cancellationToken = default);
    Task<GameSchema?> GetGameSchemaAsync(int appId, CancellationToken cancellationToken = default);
    Task<StoreGame?> GetStoreGameDetailsAsync(int appId, CancellationToken cancellationToken = default);
}
