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
    private const int PageSize = 200;

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

        var published = 0;
        var lastId = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var page = await db.Games
                .AsNoTracking()
                .Where(g => g.Id > lastId)
                .Where(g => g.Achievements.Any())
                .Where(g =>
                    g.GlobalPercentSyncedAt == null
                    || g.Achievements.Any(a => a.GlobalPercentage == null))
                .OrderBy(g => g.Id)
                .Select(g => new { g.Id, g.GameSteamId })
                .Take(PageSize)
                .ToListAsync(cancellationToken);

            if (page.Count == 0)
            {
                break;
            }

            foreach (var row in page)
            {
                lastId = row.Id;
                if (!int.TryParse(row.GameSteamId, out var appId) || appId <= 0)
                {
                    continue;
                }

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
                        "Global percent backfill progress: {Published} published (cursor game id {LastId})",
                        published,
                        lastId);
                }
            }
        }

        _logger.LogInformation("Global percent backfill finished: {Published} jobs published", published);
    }
}
