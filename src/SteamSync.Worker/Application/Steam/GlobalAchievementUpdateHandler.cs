using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SteamSync.Shared.Messages;
using SteamSync.Worker.Application.Steam.Interfaces;
using SteamSync.Worker.Infrastructure.Options;
using SteamSync.Worker.Infrastructure.Persistence;

namespace SteamSync.Worker.Application.Steam;

public class GlobalAchievementUpdateHandler
{
    private readonly WorkerDbContext _db;
    private readonly IAchievementFetcher _achievementFetcher;
    private readonly SyncWorkerOptions _options;
    private readonly ILogger<GlobalAchievementUpdateHandler> _logger;

    public GlobalAchievementUpdateHandler(
        WorkerDbContext db,
        IAchievementFetcher achievementFetcher,
        IOptions<SyncWorkerOptions> options,
        ILogger<GlobalAchievementUpdateHandler> logger)
    {
        _db = db;
        _achievementFetcher = achievementFetcher;
        _options = options.Value;
        _logger = logger;
    }

    public async Task HandleAsync(GlobalAchievementUpdateJob job, CancellationToken cancellationToken = default)
    {
        if (job.AppId <= 0)
        {
            return;
        }

        var steamAppId = job.AppId.ToString();
        var game = await _db.Games
            .FirstOrDefaultAsync(g => g.GameSteamId == steamAppId, cancellationToken);

        if (game is null)
        {
            throw new AchievementCatalogNotReadyException(job.AppId);
        }

        var ttl = TimeSpan.FromHours(Math.Max(1, _options.GlobalPercentTtlHours));
        if (game.GlobalPercentSyncedAt.HasValue &&
            game.GlobalPercentSyncedAt.Value > DateTimeOffset.UtcNow - ttl)
        {
            _logger.LogDebug("Global percent for app {AppId} is within TTL", job.AppId);
            return;
        }

        var catalog = await _db.Achievements
            .Where(a => a.GameId == game.Id)
            .ToListAsync(cancellationToken);

        if (catalog.Count == 0)
        {
            if (game.SchemaSyncedAt is null)
            {
                throw new AchievementCatalogNotReadyException(job.AppId);
            }

            game.GlobalPercentSyncedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogDebug("Global percent for app {AppId}: schema has no achievements", job.AppId);
            return;
        }

        var percentages = await _achievementFetcher.GetGlobalAchievementPercentagesAsync(job.AppId, cancellationToken);
        if (percentages is null)
        {
            throw new InvalidOperationException($"Global achievement percentages unavailable for app {job.AppId}.");
        }

        var byName = percentages
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .GroupBy(p => p.Name!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Percent, StringComparer.OrdinalIgnoreCase);

        var updated = 0;
        foreach (var achievement in catalog)
        {
            if (string.IsNullOrWhiteSpace(achievement.ApiName) ||
                !byName.TryGetValue(achievement.ApiName, out var percent))
            {
                continue;
            }

            achievement.GlobalPercentage = RoundGlobalPercentage(percent);
            updated++;
        }

        game.GlobalPercentSyncedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Updated global percentages for app {AppId}: {Updated}/{Catalog}",
            job.AppId,
            updated,
            catalog.Count);
    }

    private static decimal RoundGlobalPercentage(decimal percent)
    {
        var rounded = Math.Round(percent, 2, MidpointRounding.AwayFromZero);
        if (rounded < 0m)
        {
            return 0m;
        }

        if (rounded > 999.99m)
        {
            return 999.99m;
        }

        return rounded;
    }
}

public sealed class AchievementCatalogNotReadyException : Exception
{
    public AchievementCatalogNotReadyException(int appId)
        : base($"Achievement catalog for app {appId} is not ready.")
    {
        AppId = appId;
    }

    public int AppId { get; }
}
