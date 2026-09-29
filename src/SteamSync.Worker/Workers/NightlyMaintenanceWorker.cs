using Cronos;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SteamSync.Shared.Messages;
using SteamSync.Worker.Data;
using SteamSync.Worker.Enums;
using SteamSync.Worker.Options;

namespace SteamSync.Worker.Workers;

/// <summary>Enqueues per-user recent+crawl sync jobs on a nightly cron.</summary>
public class NightlyMaintenanceWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SyncWorkerOptions _options;
    private readonly ILogger<NightlyMaintenanceWorker> _logger;
    private readonly CronExpression _nightlyCron;
    private DateTimeOffset? _nextNightly;

    public NightlyMaintenanceWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<SyncWorkerOptions> options,
        ILogger<NightlyMaintenanceWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
        _nightlyCron = CronExpression.Parse(_options.NightlyCron);
        _nextNightly = _nightlyCron.GetNextOccurrence(DateTimeOffset.UtcNow, TimeZoneInfo.Utc);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Nightly maintenance worker started (cron={Cron})", _options.NightlyCron);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await MaybeEnqueueNightlyAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Nightly maintenance loop error");
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
        var sendEndpointProvider = scope.ServiceProvider.GetRequiredService<ISendEndpointProvider>();
        var endpoint = await sendEndpointProvider.GetSendEndpoint(new Uri($"queue:{SyncQueueNames.Jobs}"));

        var users = await db.Users.AsNoTracking()
            .Where(u => u.Status == (int)StatusEnum.Active || u.Status == (int)StatusEnum.Provisioning)
            .Where(u => u.SteamId != null)
            .Select(u => new { u.Id, u.SteamId })
            .ToListAsync(cancellationToken);

        foreach (var user in users)
        {
            await endpoint.Send(new UserSyncJob
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

        _logger.LogInformation("Enqueued nightly sync for {Count} users", users.Count);
        _nextNightly = _nightlyCron.GetNextOccurrence(now, TimeZoneInfo.Utc);
    }
}
