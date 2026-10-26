using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SteamSync.Shared.Messages;
using SteamSync.Worker.Application.Steam.Interfaces;
using SteamSync.Worker.Infrastructure.Options;
using SteamSync.Worker.Infrastructure.Persistence;

namespace SteamSync.Worker.Workers;

/// <summary>
/// One-shot enqueue of global-percent jobs for existing catalog games when
/// <see cref="SyncWorkerOptions.EnqueueGlobalPercentBackfillOnStartup"/> is true.
/// </summary>
public sealed class GlobalPercentBackfillWorker : BackgroundService
{
    private const int LogEvery = 100;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SyncWorkerOptions _options;
    private readonly ILogger<GlobalPercentBackfillWorker> _logger;

    public GlobalPercentBackfillWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<SyncWorkerOptions> options,
        ILogger<GlobalPercentBackfillWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnqueueGlobalPercentBackfillOnStartup)
        {
            _logger.LogDebug("Global percent backfill on startup is disabled");
            return;
        }

        try
        {
            await EnqueueBackfillAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Global percent backfill enqueue failed");
        }
    }

    private async Task EnqueueBackfillAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<ISyncJobPublisher>();

        var steamIds = await db.Games
            .AsNoTracking()
            .Where(g => g.Achievements.Any())
            .Where(g =>
                g.GlobalPercentSyncedAt == null
                || g.Achievements.Any(a => a.GlobalPercentage == null))
            .Select(g => g.GameSteamId)
            .ToListAsync(cancellationToken);

        var appIds = steamIds
            .Select(id => int.TryParse(id, out var appId) ? appId : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        _logger.LogInformation(
            "Global percent backfill: enqueueing {Count} apps",
            appIds.Count);

        var published = 0;
        foreach (var appId in appIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await publisher.PublishGlobalAchievementUpdateAsync(
                new GlobalAchievementUpdateJob
                {
                    JobId = Guid.NewGuid(),
                    AppId = appId
                },
                cancellationToken);

            published++;
            if (published % LogEvery == 0)
            {
                _logger.LogInformation(
                    "Global percent backfill progress: {Published}/{Total}",
                    published,
                    appIds.Count);
            }
        }

        _logger.LogInformation(
            "Global percent backfill finished: published {Published} jobs. Set EnqueueGlobalPercentBackfillOnStartup to false after the queue drains.",
            published);
    }
}
