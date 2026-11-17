using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using RabbitMQ.Client;
using SteamSync.Shared.Messages;
using SteamSync.Worker.Application.Steam.Interfaces;
using SteamSync.Worker.Infrastructure.Messaging.Interfaces;

namespace SteamSync.Worker.Infrastructure.Messaging;

public sealed class SyncJobPublisher : ISyncJobPublisher, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan GlobalDedupeTtl = TimeSpan.FromHours(6);

    private readonly IRabbitMqConnectionFactory _connectionFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SyncJobPublisher> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    public SyncJobPublisher(
        IRabbitMqConnectionFactory connectionFactory,
        IMemoryCache cache,
        ILogger<SyncJobPublisher> logger)
    {
        _connectionFactory = connectionFactory;
        _cache = cache;
        _logger = logger;
    }

    public Task PublishAsync(UserSyncJob job, CancellationToken cancellationToken = default) =>
        PublishToQueueAsync(SyncQueueNames.Jobs, job, cancellationToken);

    public Task PublishDeadLetterAsync(UserSyncJob job, CancellationToken cancellationToken = default) =>
        PublishToExchangeAsync(SyncQueueNames.DeadLetterExchange, SyncQueueNames.DeadLetterQueue, job, cancellationToken);

    public Task PublishFirstSyncAsync(FirstSyncJob job, CancellationToken cancellationToken = default) =>
        PublishToQueueAsync(SyncQueueNames.FirstSync, job, cancellationToken);

    public Task PublishFirstSyncDeadLetterAsync(FirstSyncJob job, CancellationToken cancellationToken = default) =>
        PublishToExchangeAsync(
            SyncQueueNames.FirstSyncDeadLetterExchange,
            SyncQueueNames.FirstSyncDeadLetterQueue,
            job,
            cancellationToken);

    public Task PublishGamesListSyncAsync(GamesListSyncJob job, CancellationToken cancellationToken = default) =>
        PublishToQueueAsync(SyncQueueNames.GamesListSync, job, cancellationToken);

    public Task PublishGamesListSyncDeadLetterAsync(GamesListSyncJob job, CancellationToken cancellationToken = default) =>
        PublishToExchangeAsync(
            SyncQueueNames.GamesListSyncDeadLetterExchange,
            SyncQueueNames.GamesListSyncDeadLetterQueue,
            job,
            cancellationToken);

    public Task PublishUserAchievementsSyncAsync(UserAchievementsSyncJob job, CancellationToken cancellationToken = default)
    {
        var queue = SyncJobPriorities.IsHigh(job.Priority)
            ? SyncQueueNames.UserAchievementsSyncHigh
            : SyncQueueNames.UserAchievementsSyncLow;
        return PublishToQueueAsync(queue, job, cancellationToken);
    }

    public Task PublishUserAchievementsSyncDeadLetterAsync(
        UserAchievementsSyncJob job,
        CancellationToken cancellationToken = default)
    {
        if (SyncJobPriorities.IsHigh(job.Priority))
        {
            return PublishToExchangeAsync(
                SyncQueueNames.UserAchievementsSyncHighDeadLetterExchange,
                SyncQueueNames.UserAchievementsSyncHighDeadLetterQueue,
                job,
                cancellationToken);
        }

        return PublishToExchangeAsync(
            SyncQueueNames.UserAchievementsSyncLowDeadLetterExchange,
            SyncQueueNames.UserAchievementsSyncLowDeadLetterQueue,
            job,
            cancellationToken);
    }

    public Task PublishSyncLibraryAsync(SyncLibraryJob job, CancellationToken cancellationToken = default) =>
        PublishToQueueAsync(SyncQueueNames.SyncLibrary, job, cancellationToken);

    public Task PublishSyncLibraryDeadLetterAsync(SyncLibraryJob job, CancellationToken cancellationToken = default) =>
        PublishToExchangeAsync(
            SyncQueueNames.SyncLibraryDeadLetterExchange,
            SyncQueueNames.SyncLibraryDeadLetterQueue,
            job,
            cancellationToken);

    public async Task PublishGlobalAchievementUpdateAsync(
        GlobalAchievementUpdateJob job,
        CancellationToken cancellationToken = default)
    {
        var dedupeKey = $"global-percent-enqueued:{job.AppId}";
        if (_cache.TryGetValue(dedupeKey, out _))
        {
            _logger.LogDebug("Skipping duplicate global-percent job for app {AppId}", job.AppId);
            return;
        }

        await PublishToQueueAsync(SyncQueueNames.GlobalAchievementUpdate, job, cancellationToken);
        _cache.Set(
            dedupeKey,
            true,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = GlobalDedupeTtl,
                Size = 1
            });
    }

    public Task PublishGlobalAchievementUpdateDeadLetterAsync(
        GlobalAchievementUpdateJob job,
        CancellationToken cancellationToken = default) =>
        PublishToExchangeAsync(
            SyncQueueNames.GlobalAchievementUpdateDeadLetterExchange,
            SyncQueueNames.GlobalAchievementUpdateDeadLetterQueue,
            job,
            cancellationToken);

    private async Task PublishToQueueAsync<T>(string routingKey, T job, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var channel = await EnsureChannelUnlockedAsync(cancellationToken);
            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(job, JsonOptions));
            var props = new BasicProperties { ContentType = "application/json", DeliveryMode = DeliveryModes.Persistent };
            await channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: routingKey,
                mandatory: false,
                basicProperties: props,
                body: body,
                cancellationToken: cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task PublishToExchangeAsync<T>(
        string exchange,
        string routingKey,
        T job,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var channel = await EnsureChannelUnlockedAsync(cancellationToken);
            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(job, JsonOptions));
            var props = new BasicProperties { ContentType = "application/json", DeliveryMode = DeliveryModes.Persistent };
            await channel.BasicPublishAsync(
                exchange: exchange,
                routingKey: routingKey,
                mandatory: false,
                basicProperties: props,
                body: body,
                cancellationToken: cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IChannel> EnsureChannelUnlockedAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        _connection?.Dispose();
        _channel?.Dispose();

        _connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
        _logger.LogDebug("RabbitMQ publisher channel ready");
        return _channel;
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_channel is not null)
            {
                await _channel.CloseAsync();
                _channel.Dispose();
                _channel = null;
            }

            if (_connection is not null)
            {
                await _connection.CloseAsync();
                _connection.Dispose();
                _connection = null;
            }
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}
