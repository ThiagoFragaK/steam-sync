using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using SteamSync.Shared.Messages;
using SteamSync.Worker.Application.Steam;
using SteamSync.Worker.Application.Steam.Interfaces;
using SteamSync.Worker.Infrastructure.Messaging.Interfaces;
using SteamSync.Worker.Infrastructure.Options;

namespace SteamSync.Worker.Infrastructure.Messaging;

public sealed class GamesListSyncConsumer : RetryingQueueConsumer<GamesListSyncJob>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISyncJobPublisher _publisher;

    public GamesListSyncConsumer(
        IRabbitMqConnectionFactory connectionFactory,
        IServiceScopeFactory scopeFactory,
        ISyncJobPublisher publisher,
        IOptions<SyncWorkerOptions> options,
        ILogger<GamesListSyncConsumer> logger)
        : base(connectionFactory, options, logger, SyncQueueNames.GamesListSync, prefetchCount: 10)
    {
        _scopeFactory = scopeFactory;
        _publisher = publisher;
    }

    protected override bool IsValid(GamesListSyncJob job) =>
        job.UserId > 0 && !string.IsNullOrWhiteSpace(job.SteamId);

    protected override int GetRetryCount(GamesListSyncJob job) => job.RetryCount;

    protected override void IncrementRetryCount(GamesListSyncJob job) => job.RetryCount++;

    protected override Guid GetJobId(GamesListSyncJob job) => job.JobId;

    protected override async Task HandleJobAsync(GamesListSyncJob job, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<GamesListSyncHandler>();
        await handler.HandleAsync(job, cancellationToken);
    }

    protected override Task PublishRetryAsync(GamesListSyncJob job, CancellationToken cancellationToken) =>
        _publisher.PublishGamesListSyncAsync(job, cancellationToken);

    protected override Task PublishDeadLetterAsync(GamesListSyncJob job, CancellationToken cancellationToken) =>
        _publisher.PublishGamesListSyncDeadLetterAsync(job, cancellationToken);
}
