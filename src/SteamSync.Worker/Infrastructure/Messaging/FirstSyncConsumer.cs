using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using SteamSync.Shared.Messages;
using SteamSync.Worker.Application.Steam;
using SteamSync.Worker.Application.Steam.Interfaces;
using SteamSync.Worker.Infrastructure.Messaging.Interfaces;
using SteamSync.Worker.Infrastructure.Options;

namespace SteamSync.Worker.Infrastructure.Messaging;

/// <summary>Consumes <see cref="FirstSyncJob"/> messages from the high-priority first_sync queue.</summary>
public sealed class FirstSyncConsumer : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const ushort PrefetchCount = 2;

    private readonly IRabbitMqConnectionFactory _connectionFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISyncJobPublisher _publisher;
    private readonly SyncWorkerOptions _options;
    private readonly ILogger<FirstSyncConsumer> _logger;

    public FirstSyncConsumer(
        IRabbitMqConnectionFactory connectionFactory,
        IServiceScopeFactory scopeFactory,
        ISyncJobPublisher publisher,
        IOptions<SyncWorkerOptions> options,
        ILogger<FirstSyncConsumer> logger)
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
                if (ex is OperationInterruptedException { ShutdownReason.ReplyCode: 404 })
                {
                    _logger.LogWarning("Queue {Queue} does not exist yet; retrying in 5s", SyncQueueNames.FirstSync);
                }
                else
                {
                    _logger.LogError(ex, "FirstSync RabbitMQ consumer loop faulted; reconnecting in 5s");
                }
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
                _logger.LogError(ex, "Unhandled error processing FirstSync delivery {DeliveryTag}", ea.DeliveryTag);
                try
                {
                    await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: CancellationToken.None);
                }
                catch (Exception nackEx)
                {
                    _logger.LogWarning(nackEx, "Failed to nack FirstSync delivery {DeliveryTag}", ea.DeliveryTag);
                }
            }
        };

        consumer.ShutdownAsync += (_, args) =>
        {
            _logger.LogWarning("FirstSync consumer shut down: {ReplyText}", args.ReplyText);
            tcs.TrySetResult();
            return Task.CompletedTask;
        };

        await channel.BasicConsumeAsync(
            queue: SyncQueueNames.FirstSync,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation("Consuming queue {Queue}", SyncQueueNames.FirstSync);
        await tcs.Task;
    }

    private async Task HandleDeliveryAsync(IChannel channel, BasicDeliverEventArgs ea, CancellationToken stoppingToken)
    {
        FirstSyncJob? job;
        try
        {
            job = JsonSerializer.Deserialize<FirstSyncJob>(ea.Body.Span, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Invalid FirstSyncJob JSON; sending to DLQ");
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: CancellationToken.None);
            return;
        }

        if (job is null || job.UserId <= 0 || string.IsNullOrWhiteSpace(job.SteamId))
        {
            _logger.LogError("Invalid FirstSyncJob body; sending to DLQ");
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: CancellationToken.None);
            return;
        }

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<FirstSyncHandler>();
            await handler.HandleAsync(job.UserId, job.SteamId, job.JobId, stoppingToken);
            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: CancellationToken.None);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true, cancellationToken: CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FirstSync job {JobId} failed (attempt {Attempt})", job.JobId, job.RetryCount + 1);

            if (job.RetryCount + 1 < _options.MaxAttempts)
            {
                job.RetryCount++;
                await _publisher.PublishFirstSyncAsync(job, CancellationToken.None);
                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: CancellationToken.None);
            }
            else
            {
                await _publisher.PublishFirstSyncDeadLetterAsync(job, CancellationToken.None);
                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: CancellationToken.None);
                _logger.LogError("FirstSync job {JobId} exhausted retries; published to DLX", job.JobId);
            }
        }
    }
}
