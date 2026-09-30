using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using SteamSync.Shared.Messages;
using SteamSync.Worker.MessageHandlers;
using SteamSync.Worker.Options;

namespace SteamSync.Worker.Messaging;

/// <summary>Consumes <see cref="UserSyncJob"/> JSON messages from RabbitMQ.</summary>
public sealed class UserSyncJobConsumerHostedService : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const ushort PrefetchCount = 5;

    private readonly IRabbitMqConnectionFactory _connectionFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISyncJobPublisher _publisher;
    private readonly SyncWorkerOptions _options;
    private readonly ILogger<UserSyncJobConsumerHostedService> _logger;

    public UserSyncJobConsumerHostedService(
        IRabbitMqConnectionFactory connectionFactory,
        IServiceScopeFactory scopeFactory,
        ISyncJobPublisher publisher,
        IOptions<SyncWorkerOptions> options,
        ILogger<UserSyncJobConsumerHostedService> logger)
    {
        _connectionFactory = connectionFactory;
        _scopeFactory = scopeFactory;
        _publisher = publisher;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunConsumerLoopAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RabbitMQ consumer loop faulted; reconnecting in 5s");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private async Task RunConsumerLoopAsync(CancellationToken stoppingToken)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await RabbitMqTopology.DeclareAsync(channel, stoppingToken);
        await channel.BasicQosAsync(0, PrefetchCount, false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        stoppingToken.Register(() => tcs.TrySetResult());

        consumer.ReceivedAsync += async (_, ea) =>
        {
            try
            {
                await HandleDeliveryAsync(channel, ea, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error processing delivery {DeliveryTag}", ea.DeliveryTag);
                try
                {
                    await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: CancellationToken.None);
                }
                catch (Exception nackEx)
                {
                    _logger.LogWarning(nackEx, "Failed to nack delivery {DeliveryTag}", ea.DeliveryTag);
                }
            }
        };

        consumer.ShutdownAsync += (_, args) =>
        {
            _logger.LogWarning("RabbitMQ consumer shut down: {ReplyText}", args.ReplyText);
            tcs.TrySetResult();
            return Task.CompletedTask;
        };

        await channel.BasicConsumeAsync(
            queue: SyncQueueNames.Jobs,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation("Consuming queue {Queue}", SyncQueueNames.Jobs);
        await tcs.Task;
    }

    private async Task HandleDeliveryAsync(IChannel channel, BasicDeliverEventArgs ea, CancellationToken stoppingToken)
    {
        UserSyncJob? job;
        try
        {
            job = JsonSerializer.Deserialize<UserSyncJob>(ea.Body.Span, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Invalid UserSyncJob JSON; sending to DLQ");
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: CancellationToken.None);
            return;
        }

        if (job is null)
        {
            _logger.LogError("Null UserSyncJob body; sending to DLQ");
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: CancellationToken.None);
            return;
        }

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<UserSyncMessageHandler>();
            await handler.HandleAsync(job, stoppingToken);
            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: CancellationToken.None);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true, cancellationToken: CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Job {JobId} failed (attempt {Attempt})", job.JobId, job.RetryCount + 1);

            if (job.RetryCount + 1 < _options.MaxAttempts)
            {
                job.RetryCount++;
                await _publisher.PublishAsync(job, CancellationToken.None);
                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: CancellationToken.None);
            }
            else
            {
                await _publisher.PublishDeadLetterAsync(job, CancellationToken.None);
                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: CancellationToken.None);
                _logger.LogError("Job {JobId} exhausted retries; published to DLX", job.JobId);
            }
        }
    }
}
