using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using SteamSync.Shared.Messages;
using SteamSync.Worker.Application.Steam;
using SteamSync.Worker.Application.Steam.Interfaces;
using SteamSync.Worker.Infrastructure.Messaging.Interfaces;
using SteamSync.Worker.Infrastructure.Options;

namespace SteamSync.Worker.Infrastructure.Messaging;

public sealed class SyncLibraryConsumer : RetryingQueueConsumer<SyncLibraryJob>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISyncJobPublisher _publisher;

    public SyncLibraryConsumer(
        IRabbitMqConnectionFactory connectionFactory,
        IServiceScopeFactory scopeFactory,
        ISyncJobPublisher publisher,
        IOptions<SyncWorkerOptions> options,
        ILogger<SyncLibraryConsumer> logger)
        : base(connectionFactory, options, logger, SyncQueueNames.SyncLibrary, prefetchCount: 2)
    {
        _scopeFactory = scopeFactory;
        _publisher = publisher;
    }

    protected override bool IsValid(SyncLibraryJob job) =>
        job.UserId > 0 && !string.IsNullOrWhiteSpace(job.SteamId);

    protected override int GetRetryCount(SyncLibraryJob job) => job.RetryCount;

    protected override void IncrementRetryCount(SyncLibraryJob job) => job.RetryCount++;

    protected override Guid GetJobId(SyncLibraryJob job) => job.JobId;

    protected override async Task HandleJobAsync(SyncLibraryJob job, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<SyncLibraryHandler>();
        await handler.HandleAsync(job, cancellationToken);
    }

    protected override Task PublishRetryAsync(SyncLibraryJob job, CancellationToken cancellationToken) =>
        _publisher.PublishSyncLibraryAsync(job, cancellationToken);

    protected override Task PublishDeadLetterAsync(SyncLibraryJob job, CancellationToken cancellationToken) =>
        _publisher.PublishSyncLibraryDeadLetterAsync(job, cancellationToken);
}
