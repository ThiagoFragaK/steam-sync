using Cronos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SteamSync.Shared.Messages;
using SteamSync.Worker.Infrastructure.Persistence;
using SteamSync.Worker.Domain.Enums;
using SteamSync.Worker.Application.Steam;
using SteamSync.Worker.Application.Steam.Interfaces;
using SteamSync.Worker.Infrastructure.Messaging;
using SteamSync.Worker.Infrastructure.Messaging.Interfaces;
using SteamSync.Worker.Infrastructure.Options;

namespace SteamSync.Worker.Workers;

/// <summary>Enqueues per-user recent+crawl sync jobs on a daily cron.</summary>
public class DailyUpdateWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SyncWorkerOptions _options;
    private readonly ILogger<DailyUpdateWorker> _logger;
    private readonly CronExpression _nightlyCron;
    private DateTimeOffset? _nextNightly;

    public DailyUpdateWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<SyncWorkerOptions> options,
        ILogger<DailyUpdateWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
        _nightlyCron = CronExpression.Parse(_options.NightlyCron);
        _nextNightly = _nightlyCron.GetNextOccurrence(DateTimeOffset.UtcNow, TimeZoneInfo.Utc);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Daily update worker started (cron={Cron})", _options.NightlyCron);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await MaybeEnqueueNightlyAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Daily update loop error");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task MaybeEnqueueNightlyAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (_nextNightly is null || now < _nextNightly)
        {
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<ISyncJobPublisher>();

        var users = await db.Users.AsNoTracking()
            .Where(u => u.Status == (int)StatusEnum.Active || u.Status == (int)StatusEnum.Provisioning)
            .Where(u => u.SteamId != null)
            .Select(u => new { u.Id, u.SteamId })
            .ToListAsync(cancellationToken);

        foreach (var user in users)
        {
            await publisher.PublishAsync(new UserSyncJob
            {
                JobType = SyncJobTypes.RecentActivityOnly,
                Payload = new UserSyncPayload
                {
                    UserId = user.Id,
                    SteamId = user.SteamId!,
                    Priority = "low",
                    IncludeCrawl = true
                }
            }, cancellationToken);
        }

        _logger.LogInformation("Enqueued daily sync for {Count} users", users.Count);
        _nextNightly = _nightlyCron.GetNextOccurrence(now, TimeZoneInfo.Utc);
    }
}
