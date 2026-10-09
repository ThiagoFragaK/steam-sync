using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using SteamSync.Shared.Messages;
using SteamSync.Worker.Application.Steam;
using SteamSync.Worker.Application.Steam.Interfaces;
using SteamSync.Worker.Infrastructure.Messaging.Interfaces;
using SteamSync.Worker.Infrastructure.Options;

namespace SteamSync.Worker.Infrastructure.Messaging;

/// <summary>Shared consumer for high/low user achievements sync queues.</summary>
public abstract class UserAchievementsSyncConsumerBase : RetryingQueueConsumer<UserAchievementsSyncJob>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISyncJobPublisher _publisher;

    protected UserAchievementsSyncConsumerBase(
        IRabbitMqConnectionFactory connectionFactory,
        IServiceScopeFactory scopeFactory,
        ISyncJobPublisher publisher,
        IOptions<SyncWorkerOptions> options,
        ILogger logger,
        string queueName)
        : base(connectionFactory, options, logger, queueName, prefetchCount: 5)
    {
        _scopeFactory = scopeFactory;
        _publisher = publisher;
    }

    protected override bool IsValid(UserAchievementsSyncJob job) =>
        job.UserId > 0 && !string.IsNullOrWhiteSpace(job.SteamId) && job.AppId > 0;

    protected override int GetRetryCount(UserAchievementsSyncJob job) => job.RetryCount;

    protected override void IncrementRetryCount(UserAchievementsSyncJob job) => job.RetryCount++;

    protected override Guid GetJobId(UserAchievementsSyncJob job) => job.JobId;

    protected override async Task HandleJobAsync(UserAchievementsSyncJob job, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<UserAchievementsSyncHandler>();
        await handler.HandleAsync(job, cancellationToken);
    }

    protected override Task PublishRetryAsync(UserAchievementsSyncJob job, CancellationToken cancellationToken) =>
        _publisher.PublishUserAchievementsSyncAsync(job, cancellationToken);

    protected override Task PublishDeadLetterAsync(UserAchievementsSyncJob job, CancellationToken cancellationToken) =>
        _publisher.PublishUserAchievementsSyncDeadLetterAsync(job, cancellationToken);

    protected override async Task OnRetriesExhaustedAsync(UserAchievementsSyncJob job, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<UserAchievementsSyncHandler>();
        var syncRepository = scope.ServiceProvider.GetRequiredService<ISyncRepository>();

        // Surface the error but keep Syncing so Register polling continues.
        await syncRepository.SetLastErrorAsync(
            job.UserId,
            $"Achievement sync failed for app {job.AppId} after retries",
            cancellationToken);

        await handler.CountFailedAttemptAsync(job.UserId, job.SteamId, cancellationToken);
    }
}

public sealed class UserAchievementsSyncHighConsumer : UserAchievementsSyncConsumerBase
{
    public UserAchievementsSyncHighConsumer(
        IRabbitMqConnectionFactory connectionFactory,
        IServiceScopeFactory scopeFactory,
        ISyncJobPublisher publisher,
        IOptions<SyncWorkerOptions> options,
        ILogger<UserAchievementsSyncHighConsumer> logger)
        : base(
            connectionFactory,
            scopeFactory,
            publisher,
            options,
            logger,
            SyncQueueNames.UserAchievementsSyncHigh)
    {
    }
}

public sealed class UserAchievementsSyncLowConsumer : UserAchievementsSyncConsumerBase
{
    public UserAchievementsSyncLowConsumer(
        IRabbitMqConnectionFactory connectionFactory,
        IServiceScopeFactory scopeFactory,
        ISyncJobPublisher publisher,
        IOptions<SyncWorkerOptions> options,
        ILogger<UserAchievementsSyncLowConsumer> logger)
        : base(
            connectionFactory,
            scopeFactory,
            publisher,
            options,
            logger,
            SyncQueueNames.UserAchievementsSyncLow)
    {
    }
}
