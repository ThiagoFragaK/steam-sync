using SteamSync.Worker.Data;
using SteamSync.Worker.Entities;
using SteamSync.Worker.Options;
using SteamSync.Worker.Services;
using SteamSync.Worker.Services.Interfaces;
using SteamSync.Worker.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace SteamSync.Worker.Services;

public class SteamSyncService : ISteamSyncService
{
    private readonly WorkerDbContext _db;
    private readonly IAchievementFetcher _achievementFetcher;
    private readonly IMemoryCache _cache;
    private readonly SyncWorkerOptions _options;
    private readonly ILogger<SteamSyncService> _logger;

    public SteamSyncService(
        WorkerDbContext db,
        IAchievementFetcher achievementFetcher,
        IMemoryCache cache,
        IOptions<SyncWorkerOptions> options,
        ILogger<SteamSyncService> logger)
    {
        _db = db;
        _achievementFetcher = achievementFetcher;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SyncLibraryAsync(
        int userId,
        string steamId,
        LibrarySyncScope scope = LibrarySyncScope.Full,
        int maxStoreEnrich = 5,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(steamId))
        {
            throw new ArgumentException("steamId is required.", nameof(steamId));
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new InvalidOperationException($"User {userId} was not found.");

        var recentGames = await _achievementFetcher.GetRecentlyPlayedGamesAsync(steamId, cancellationToken);
        var byAppId = new Dictionary<int, SyncGameInput>();

        if (scope == LibrarySyncScope.Full)
        {
            var ownedGames = await _achievementFetcher.GetOwnedGamesAsync(steamId, cancellationToken);
            foreach (var owned in ownedGames)
            {
                if (owned.AppId <= 0)
                {
                    continue;
                }

                byAppId[owned.AppId] = new SyncGameInput(
                    owned.AppId,
                    owned.Name,
                    owned.ImgIconUrl,
                    owned.PlaytimeForeverMinutes,
                    owned.LastPlayedUnix,
                    owned.HasCommunityVisibleStats);
            }
        }

        foreach (var recent in recentGames)
        {
            if (recent.AppId <= 0)
            {
                continue;
            }

            if (byAppId.TryGetValue(recent.AppId, out var existing))
            {
                byAppId[recent.AppId] = existing with
                {
                    Name = string.IsNullOrWhiteSpace(existing.Name) ? recent.Name : existing.Name,
                    ImageUrl = string.IsNullOrWhiteSpace(existing.ImageUrl) ? recent.ImgIconUrl : existing.ImageUrl,
                    PlaytimeMinutes = existing.PlaytimeMinutes > 0
                        ? existing.PlaytimeMinutes
                        : recent.PlaytimeForeverMinutes
                };
            }
            else
            {
                byAppId[recent.AppId] = new SyncGameInput(
                    recent.AppId,
                    recent.Name,
                    recent.ImgIconUrl,
                    recent.PlaytimeForeverMinutes,
                    null,
                    null);
            }
        }

        if (byAppId.Count == 0)
        {
            user.Playtime2WeeksMinutes = recentGames.Sum(g => g.Playtime2WeeksMinutes);
            await _db.SaveChangesAsync(cancellationToken);
            _cache.Remove(RecentGamesCacheKeys.ForSteamId(steamId));
            _logger.LogInformation(
                "Synced library ({Scope}) for user {UserId}: 0 games",
                scope,
                userId);
            return;
        }

        var steamIds = byAppId.Keys.Select(id => id.ToString()).ToList();
        var existingGames = await _db.Games
            .Where(g => g.GameSteamId != null && steamIds.Contains(g.GameSteamId))
            .ToListAsync(cancellationToken);

        var gamesBySteamId = existingGames
            .Where(g => g.GameSteamId is not null)
            .ToDictionary(g => g.GameSteamId!, StringComparer.Ordinal);

        var storeEnrichBudget = Math.Max(0, maxStoreEnrich);

        foreach (var input in byAppId.Values)
        {
            var steamAppId = input.AppId.ToString();
            if (!gamesBySteamId.TryGetValue(steamAppId, out var game))
            {
                game = new Game
                {
                    GameSteamId = steamAppId,
                    Name = string.IsNullOrWhiteSpace(input.Name) ? steamAppId : input.Name.Trim(),
                    ImageUrl = input.ImageUrl,
                    HasCommunityVisibleStats = input.HasCommunityVisibleStats
                };
                _db.Games.Add(game);
                gamesBySteamId[steamAppId] = game;
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(input.Name))
                {
                    game.Name = input.Name.Trim();
                }

                if (!string.IsNullOrWhiteSpace(input.ImageUrl))
                {
                    game.ImageUrl = input.ImageUrl;
                }

                if (input.HasCommunityVisibleStats.HasValue)
                {
                    game.HasCommunityVisibleStats = input.HasCommunityVisibleStats;
                }
            }

            if (storeEnrichBudget > 0 && NeedsStoreEnrichment(game))
            {
                await EnrichGameFromStoreIfNeededAsync(game, input.AppId, cancellationToken);
                storeEnrichBudget--;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        var persistedGameIds = gamesBySteamId.Values.Select(g => g.Id).ToList();
        var existingUsersGames = await _db.UsersGames
            .Where(ug => ug.UserId == userId && persistedGameIds.Contains(ug.GameId))
            .ToListAsync(cancellationToken);

        var usersGamesByGameId = existingUsersGames.ToDictionary(ug => ug.GameId);

        foreach (var input in byAppId.Values)
        {
            var steamAppId = input.AppId.ToString();
            var game = gamesBySteamId[steamAppId];

            if (!usersGamesByGameId.TryGetValue(game.Id, out var usersGame))
            {
                usersGame = new UsersGame
                {
                    UserId = userId,
                    GameId = game.Id,
                    AchievementsPercentage = 0,
                    NeedsAchievementRefresh = game.HasCommunityVisibleStats == true
                };
                _db.UsersGames.Add(usersGame);
                usersGamesByGameId[game.Id] = usersGame;
            }
            else
            {
                var playtimeChanged = usersGame.PlaytimeMinutes != input.PlaytimeMinutes;
                var lastPlayedChanged = input.LastPlayedUnix.HasValue
                    && usersGame.LastPlayedUnix != input.LastPlayedUnix;

                if ((playtimeChanged || lastPlayedChanged) && game.HasCommunityVisibleStats == true)
                {
                    usersGame.NeedsAchievementRefresh = true;
                }
            }

            usersGame.PlaytimeMinutes = input.PlaytimeMinutes;
            if (input.LastPlayedUnix.HasValue)
            {
                usersGame.LastPlayedUnix = input.LastPlayedUnix;
            }
        }

        user.Playtime2WeeksMinutes = recentGames.Sum(g => g.Playtime2WeeksMinutes);

        await _db.SaveChangesAsync(cancellationToken);

        _cache.Remove(RecentGamesCacheKeys.ForSteamId(steamId));

        _logger.LogInformation(
            "Synced library ({Scope}) for user {UserId}: {GameCount} games, Playtime2WeeksMinutes={Playtime2Weeks}",
            scope,
            userId,
            byAppId.Count,
            user.Playtime2WeeksMinutes);
    }

    public async Task<bool> SyncGameCompletionPercentageAsync(
        int userId,
        string steamId,
        int appId,
        CancellationToken cancellationToken = default)
    {
        if (appId <= 0)
        {
            return false;
        }

        var steamAppId = appId.ToString();
        var game = await _db.Games
            .FirstOrDefaultAsync(g => g.GameSteamId == steamAppId, cancellationToken);

        if (game is null || game.HasCommunityVisibleStats != true)
        {
            return false;
        }

        var usersGame = await _db.UsersGames
            .FirstOrDefaultAsync(ug => ug.UserId == userId && ug.GameId == game.Id, cancellationToken);

        if (usersGame is null)
        {
            return false;
        }

        var playerResult = await _achievementFetcher.GetPlayerAchievementsAsync(steamId, appId, cancellationToken);
        if (playerResult is null || !playerResult.Success)
        {
            _logger.LogWarning(
                "Player achievements unavailable for completion % user {UserId} app {AppId}: {Error}",
                userId,
                appId,
                playerResult?.Error);
            usersGame.AchievementSyncUnavailable = true;
            usersGame.NeedsAchievementRefresh = false;
            usersGame.AchievementsSyncedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            await RecomputeUserAchievementStatsAsync(userId, cancellationToken);
            return false;
        }

        var total = playerResult.Achievements.Count;
        var unlocked = playerResult.Achievements.Count(a => a.Achieved == 1);
        usersGame.AchievementsPercentage = total == 0
            ? 0
            : (decimal)Math.Round(unlocked / (double)total * 100, 2);
        usersGame.AchievementsSyncedAt = DateTimeOffset.UtcNow;
        usersGame.NeedsAchievementRefresh = false;
        usersGame.AchievementSyncUnavailable = false;

        await _db.SaveChangesAsync(cancellationToken);
        await RecomputeUserAchievementStatsAsync(userId, cancellationToken);
        return true;
    }

    public async Task SyncGameAchievementsAsync(
        int userId,
        string steamId,
        int appId,
        CancellationToken cancellationToken = default)
    {
        if (appId <= 0)
        {
            return;
        }

        var steamAppId = appId.ToString();
        var game = await _db.Games
            .FirstOrDefaultAsync(g => g.GameSteamId == steamAppId, cancellationToken);

        if (game is null || game.HasCommunityVisibleStats != true)
        {
            _logger.LogDebug(
                "Skipping achievement sync for app {AppId}: game missing or no community stats",
                appId);
            return;
        }

        var usersGame = await _db.UsersGames
            .FirstOrDefaultAsync(ug => ug.UserId == userId && ug.GameId == game.Id, cancellationToken);

        if (usersGame is null)
        {
            _logger.LogDebug(
                "Skipping achievement sync for app {AppId}: no UsersGame for user {UserId}",
                appId,
                userId);
            return;
        }

        var schemaTtl = TimeSpan.FromDays(Math.Max(1, _options.SchemaTtlDays));
        var schemaFresh = game.SchemaSyncedAt.HasValue
            && game.SchemaSyncedAt.Value > DateTimeOffset.UtcNow - schemaTtl
            && await _db.Achievements.AnyAsync(a => a.GameId == game.Id, cancellationToken);

        if (!schemaFresh)
        {
            var schema = await _achievementFetcher.GetGameSchemaAsync(appId, cancellationToken);
            var schemaAchievements = (schema?.Achievements ?? [])
                .Where(a => !string.IsNullOrWhiteSpace(a.Name))
                .GroupBy(a => a.Name!, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            var existingCatalog = await _db.Achievements
                .Where(a => a.GameId == game.Id)
                .ToListAsync(cancellationToken);

            var catalogByApiName = existingCatalog
                .Where(a => !string.IsNullOrWhiteSpace(a.ApiName))
                .ToDictionary(a => a.ApiName!, StringComparer.OrdinalIgnoreCase);

            foreach (var schemaRow in schemaAchievements)
            {
                var apiName = schemaRow.Name!;
                if (!catalogByApiName.TryGetValue(apiName, out var achievement))
                {
                    achievement = new Achievement
                    {
                        GameId = game.Id,
                        ApiName = apiName
                    };
                    _db.Achievements.Add(achievement);
                    catalogByApiName[apiName] = achievement;
                }

                achievement.Name = string.IsNullOrWhiteSpace(schemaRow.DisplayName)
                    ? apiName
                    : schemaRow.DisplayName.Trim();
                achievement.Description = schemaRow.Hidden == 1 && string.IsNullOrWhiteSpace(schemaRow.Description)
                    ? null
                    : schemaRow.Description;
                achievement.ImageUrlLock = schemaRow.IconGray;
                achievement.ImageUrlUnlock = schemaRow.Icon;
            }

            game.SchemaSyncedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        var playerResult = await _achievementFetcher.GetPlayerAchievementsAsync(steamId, appId, cancellationToken);
        if (playerResult is null || !playerResult.Success)
        {
            _logger.LogWarning(
                "Player achievements unavailable for user {UserId} app {AppId}: {Error}",
                userId,
                appId,
                playerResult?.Error);
            usersGame.AchievementSyncUnavailable = true;
            usersGame.NeedsAchievementRefresh = false;
            usersGame.AchievementsSyncedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            await RecomputeUserAchievementStatsAsync(userId, cancellationToken);
            return;
        }

        var unlockedApiNames = playerResult.Achievements
            .Where(a => a.Achieved == 1 && !string.IsNullOrWhiteSpace(a.ApiName))
            .ToDictionary(a => a.ApiName!, a => a, StringComparer.OrdinalIgnoreCase);

        var refreshedCatalog = await _db.Achievements
            .Where(a => a.GameId == game.Id)
            .ToListAsync(cancellationToken);

        var achievementIdsByApiName = refreshedCatalog
            .Where(a => !string.IsNullOrWhiteSpace(a.ApiName))
            .ToDictionary(a => a.ApiName!, a => a.Id, StringComparer.OrdinalIgnoreCase);

        var existingUnlocks = await _db.UsersAchievements
            .Where(ua => ua.UserId == userId && ua.GameId == game.Id)
            .ToListAsync(cancellationToken);

        var unlocksByAchievementId = existingUnlocks.ToDictionary(ua => ua.AchievementId);
        var unlockedAchievementIds = new HashSet<int>();

        foreach (var (apiName, playerRow) in unlockedApiNames)
        {
            if (!achievementIdsByApiName.TryGetValue(apiName, out var achievementId))
            {
                continue;
            }

            unlockedAchievementIds.Add(achievementId);

            if (!unlocksByAchievementId.TryGetValue(achievementId, out var usersAchievement))
            {
                usersAchievement = new UsersAchievement
                {
                    UserId = userId,
                    GameId = game.Id,
                    AchievementId = achievementId
                };
                _db.UsersAchievements.Add(usersAchievement);
            }

            usersAchievement.UnlockDate = playerRow.UnlockTimeUnix > 0
                ? DateTimeOffset.FromUnixTimeSeconds(playerRow.UnlockTimeUnix).UtcDateTime
                : null;
        }

        foreach (var stale in existingUnlocks.Where(ua => !unlockedAchievementIds.Contains(ua.AchievementId)))
        {
            _db.UsersAchievements.Remove(stale);
        }

        var totalAchievements = refreshedCatalog.Count > 0
            ? refreshedCatalog.Count
            : playerResult.Achievements.Count;
        var unlockedCount = unlockedAchievementIds.Count > 0
            ? unlockedAchievementIds.Count
            : playerResult.Achievements.Count(a => a.Achieved == 1);

        usersGame.AchievementsPercentage = totalAchievements == 0
            ? 0
            : (decimal)Math.Round(unlockedCount / (double)totalAchievements * 100, 2);
        usersGame.AchievementsSyncedAt = DateTimeOffset.UtcNow;
        usersGame.NeedsAchievementRefresh = false;
        usersGame.AchievementSyncUnavailable = false;

        await _db.SaveChangesAsync(cancellationToken);
        await RecomputeUserAchievementStatsAsync(userId, cancellationToken);

        _logger.LogInformation(
            "Synced achievements for user {UserId} app {AppId}: {Unlocked}/{Total}",
            userId,
            appId,
            unlockedCount,
            totalAchievements);
    }

    public async Task SyncAchievementsForUserAsync(
        int userId,
        string steamId,
        AchievementSyncScope scope,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<int> appIds = scope switch
        {
            AchievementSyncScope.AllOwnedWithStats => await GetAllOwnedWithStatsAppIdsAsync(userId, cancellationToken),
            AchievementSyncScope.RecentTwoWeeks => await GetRecentTwoWeeksAppIdsAsync(steamId, cancellationToken),
            _ => Array.Empty<int>()
        };

        foreach (var appId in appIds)
        {
            try
            {
                await SyncGameCompletionPercentageAsync(userId, steamId, appId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Achievement sync failed for user {UserId} app {AppId}", userId, appId);
            }
        }
    }

    public async Task RecomputeUserAchievementStatsAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return;
        }

        var statsRows = await _db.UsersGames
            .AsNoTracking()
            .Where(ug => ug.UserId == userId && ug.Game.HasCommunityVisibleStats == true)
            .Select(ug => new
            {
                ug.AchievementsSyncedAt,
                ug.AchievementsPercentage,
                ug.AchievementSyncUnavailable
            })
            .ToListAsync(cancellationToken);

        var ownedWithStats = statsRows.Count;
        var synced = statsRows.Count(r => r.AchievementsSyncedAt != null && !r.AchievementSyncUnavailable);

        user.AchievementSyncCoverage = ownedWithStats == 0
            ? 0
            : (decimal)Math.Round(synced / (double)ownedWithStats * 100, 2);

        user.AvgPercentage = synced == 0
            ? 0
            : Math.Round(
                statsRows
                    .Where(r => r.AchievementsSyncedAt != null && !r.AchievementSyncUnavailable)
                    .Average(r => r.AchievementsPercentage),
                2);

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<int>> GetPriorityCompletionAppIdsAsync(
        int userId,
        string steamId,
        CancellationToken cancellationToken = default)
    {
        var ordered = new List<int>();
        var seen = new HashSet<int>();

        void AddRange(IEnumerable<int> ids)
        {
            foreach (var id in ids)
            {
                if (id > 0 && seen.Add(id))
                {
                    ordered.Add(id);
                }
            }
        }

        var goalAppIds = await _db.Goals
            .AsNoTracking()
            .Where(g => g.UserId == userId && g.Game.GameSteamId != null)
            .Select(g => g.Game.GameSteamId!)
            .ToListAsync(cancellationToken);
        AddRange(ParseAppIds(goalAppIds));

        AddRange(await GetRecentTwoWeeksAppIdsAsync(steamId, cancellationToken));

        var dirtySteamIds = await _db.UsersGames
            .AsNoTracking()
            .Where(ug => ug.UserId == userId
                && ug.NeedsAchievementRefresh
                && !ug.AchievementSyncUnavailable
                && ug.Game.HasCommunityVisibleStats == true
                && ug.Game.GameSteamId != null)
            .Select(ug => ug.Game.GameSteamId!)
            .ToListAsync(cancellationToken);
        AddRange(ParseAppIds(dirtySteamIds));

        var neverSyncedPlayed = await _db.UsersGames
            .AsNoTracking()
            .Where(ug => ug.UserId == userId
                && ug.AchievementsSyncedAt == null
                && !ug.AchievementSyncUnavailable
                && ug.PlaytimeMinutes > 0
                && ug.Game.HasCommunityVisibleStats == true
                && ug.Game.GameSteamId != null)
            .OrderByDescending(ug => ug.PlaytimeMinutes)
            .Select(ug => ug.Game.GameSteamId!)
            .ToListAsync(cancellationToken);
        AddRange(ParseAppIds(neverSyncedPlayed));

        return ordered;
    }

    public async Task<IReadOnlyList<int>> GetCrawlCompletionAppIdsAsync(
        int userId,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        var steamIds = await _db.UsersGames
            .AsNoTracking()
            .Where(ug => ug.UserId == userId
                && ug.AchievementsSyncedAt == null
                && !ug.AchievementSyncUnavailable
                && ug.Game.HasCommunityVisibleStats == true
                && ug.Game.GameSteamId != null)
            .OrderByDescending(ug => ug.PlaytimeMinutes)
            .ThenBy(ug => ug.Id)
            .Skip(Math.Max(0, skip))
            .Take(Math.Max(1, take))
            .Select(ug => ug.Game.GameSteamId!)
            .ToListAsync(cancellationToken);

        return ParseAppIds(steamIds);
    }

    public Task<int> CountEligibleCrawlGamesAsync(int userId, CancellationToken cancellationToken = default) =>
        _db.UsersGames.AsNoTracking()
            .CountAsync(
                ug => ug.UserId == userId
                    && ug.AchievementsSyncedAt == null
                    && !ug.AchievementSyncUnavailable
                    && ug.Game.HasCommunityVisibleStats == true,
                cancellationToken);

    private async Task<List<int>> GetAllOwnedWithStatsAppIdsAsync(int userId, CancellationToken cancellationToken)
    {
        var steamIds = await _db.UsersGames
            .AsNoTracking()
            .Where(ug => ug.UserId == userId && ug.Game.HasCommunityVisibleStats == true)
            .Select(ug => ug.Game.GameSteamId)
            .ToListAsync(cancellationToken);

        return ParseAppIds(steamIds.Where(id => id is not null).Select(id => id!)).ToList();
    }

    private async Task<List<int>> GetRecentTwoWeeksAppIdsAsync(string steamId, CancellationToken cancellationToken)
    {
        var recent = await _achievementFetcher.GetRecentlyPlayedGamesAsync(steamId, cancellationToken);
        return recent
            .Where(g => g.AppId > 0 && g.Playtime2WeeksMinutes > 0)
            .Select(g => g.AppId)
            .Distinct()
            .ToList();
    }

    private static List<int> ParseAppIds(IEnumerable<string> steamIds)
    {
        var appIds = new List<int>();
        foreach (var id in steamIds)
        {
            if (int.TryParse(id, out var appId) && appId > 0)
            {
                appIds.Add(appId);
            }
        }

        return appIds;
    }

    private static bool NeedsStoreEnrichment(Game game) =>
        string.IsNullOrWhiteSpace(game.HeaderImageUrl)
        || string.IsNullOrWhiteSpace(game.Developers)
        || string.IsNullOrWhiteSpace(game.Publishers);

    private async Task EnrichGameFromStoreIfNeededAsync(
        Game game,
        int appId,
        CancellationToken cancellationToken)
    {
        var needsHeader = string.IsNullOrWhiteSpace(game.HeaderImageUrl);
        var needsDevelopers = string.IsNullOrWhiteSpace(game.Developers);
        var needsPublishers = string.IsNullOrWhiteSpace(game.Publishers);
        if (!needsHeader && !needsDevelopers && !needsPublishers)
        {
            return;
        }

        try
        {
            var store = await _achievementFetcher.GetStoreGameDetailsAsync(appId, cancellationToken);
            if (store is null)
            {
                _logger.LogWarning(
                    "Store enrichment skipped for app {AppId}: appdetails returned null",
                    appId);
                return;
            }

            if (needsHeader && !string.IsNullOrWhiteSpace(store.HeaderImage))
            {
                game.HeaderImageUrl = store.HeaderImage;
            }

            if (needsDevelopers && store.Developers.Count > 0)
            {
                game.Developers = string.Join(", ", store.Developers.Where(s => !string.IsNullOrWhiteSpace(s)));
            }

            if (needsPublishers && store.Publishers.Count > 0)
            {
                game.Publishers = string.Join(", ", store.Publishers.Where(s => !string.IsNullOrWhiteSpace(s)));
            }

            var nameIsPlaceholder = string.IsNullOrWhiteSpace(game.Name)
                || string.Equals(game.Name, game.GameSteamId, StringComparison.Ordinal);
            if (nameIsPlaceholder && !string.IsNullOrWhiteSpace(store.Name))
            {
                game.Name = store.Name.Trim();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Store enrichment failed for app {AppId}", appId);
        }
    }

    private sealed record SyncGameInput(
        int AppId,
        string? Name,
        string? ImageUrl,
        int PlaytimeMinutes,
        long? LastPlayedUnix,
        bool? HasCommunityVisibleStats);
}
