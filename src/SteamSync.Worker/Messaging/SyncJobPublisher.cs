using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using SteamSync.Shared.Messages;

namespace SteamSync.Worker.Messaging;

public sealed class SyncJobPublisher : ISyncJobPublisher, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IRabbitMqConnectionFactory _connectionFactory;
    private readonly ILogger<SyncJobPublisher> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    public SyncJobPublisher(IRabbitMqConnectionFactory connectionFactory, ILogger<SyncJobPublisher> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task PublishAsync(UserSyncJob job, CancellationToken cancellationToken = default)
    {
        var channel = await EnsureChannelAsync(cancellationToken);
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(job, JsonOptions));
        var props = new BasicProperties { ContentType = "application/json", DeliveryMode = DeliveryModes.Persistent };
        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: SyncQueueNames.Jobs,
            mandatory: false,
            basicProperties: props,
            body: body,
            cancellationToken: cancellationToken);
    }

    public async Task PublishDeadLetterAsync(UserSyncJob job, CancellationToken cancellationToken = default)
    {
        var channel = await EnsureChannelAsync(cancellationToken);
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(job, JsonOptions));
        var props = new BasicProperties { ContentType = "application/json", DeliveryMode = DeliveryModes.Persistent };
        await channel.BasicPublishAsync(
            exchange: SyncQueueNames.DeadLetterExchange,
            routingKey: SyncQueueNames.DeadLetterQueue,
            mandatory: false,
            basicProperties: props,
            body: body,
            cancellationToken: cancellationToken);
    }

    private async Task<IChannel> EnsureChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_channel is { IsOpen: true })
            {
                return _channel;
            }

            _connection?.Dispose();
            _channel?.Dispose();

            _connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
            _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
            await RabbitMqTopology.DeclareAsync(_channel, cancellationToken);
            _logger.LogDebug("RabbitMQ publisher channel ready");
            return _channel;
        }
        finally
        {
            _gate.Release();
        }
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
