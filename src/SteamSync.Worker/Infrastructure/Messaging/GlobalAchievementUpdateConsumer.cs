using Microsoft.Extensions.Options;
using SteamSync.Shared.Messages;
using SteamSync.Worker.Application.Steam;
using SteamSync.Worker.Application.Steam.Interfaces;
using SteamSync.Worker.Infrastructure.Messaging.Interfaces;
using SteamSync.Worker.Infrastructure.Options;

namespace SteamSync.Worker.Infrastructure.Messaging;

public sealed class GlobalAchievementUpdateConsumer : RetryingQueueConsumer<GlobalAchievementUpdateJob>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISyncJobPublisher _publisher;
    private readonly ILogger<GlobalAchievementUpdateConsumer> _logger;

    public GlobalAchievementUpdateConsumer(
        IRabbitMqConnectionFactory connectionFactory,
        IServiceScopeFactory scopeFactory,
        ISyncJobPublisher publisher,
        IOptions<SyncWorkerOptions> options,
        ILogger<GlobalAchievementUpdateConsumer> logger)
        : base(connectionFactory, options, logger, SyncQueueNames.GlobalAchievementUpdate, prefetchCount: 5)
    {
        _scopeFactory = scopeFactory;
        _publisher = publisher;
        _logger = logger;
    }

    protected override bool IsValid(GlobalAchievementUpdateJob job) => job.AppId > 0;

    protected override int GetRetryCount(GlobalAchievementUpdateJob job) => job.RetryCount;

    protected override void IncrementRetryCount(GlobalAchievementUpdateJob job) => job.RetryCount++;

    protected override Guid GetJobId(GlobalAchievementUpdateJob job) => job.JobId;

    protected override async Task HandleJobAsync(GlobalAchievementUpdateJob job, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<GlobalAchievementUpdateHandler>();
        await handler.HandleAsync(job, cancellationToken);
    }

    protected override Task PublishRetryAsync(GlobalAchievementUpdateJob job, CancellationToken cancellationToken) =>
        _publisher.PublishGlobalAchievementUpdateAsync(job, cancellationToken);

    protected override Task PublishDeadLetterAsync(GlobalAchievementUpdateJob job, CancellationToken cancellationToken) =>
        _publisher.PublishGlobalAchievementUpdateDeadLetterAsync(job, cancellationToken);

    protected override Task OnRetriesExhaustedAsync(GlobalAchievementUpdateJob job, CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "Global achievement update for app {AppId} exhausted retries",
            job.AppId);
        return Task.CompletedTask;
    }
}
