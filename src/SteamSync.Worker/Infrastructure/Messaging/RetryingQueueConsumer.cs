using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using SteamSync.Worker.Infrastructure.Messaging.Interfaces;
using SteamSync.Worker.Infrastructure.Options;

namespace SteamSync.Worker.Infrastructure.Messaging;

/// <summary>Shared RabbitMQ consumer loop with app-level retry + DLQ publish.</summary>
public abstract class RetryingQueueConsumer<TJob> : BackgroundService
    where TJob : class
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IRabbitMqConnectionFactory _connectionFactory;
    private readonly SyncWorkerOptions _options;
    private readonly ILogger _logger;
    private readonly string _queueName;
    private readonly ushort _prefetchCount;

    protected RetryingQueueConsumer(
        IRabbitMqConnectionFactory connectionFactory,
        IOptions<SyncWorkerOptions> options,
        ILogger logger,
        string queueName,
        ushort prefetchCount)
    {
        _connectionFactory = connectionFactory;
        _options = options.Value;
        _logger = logger;
        _queueName = queueName;
        _prefetchCount = prefetchCount;
    }

    protected abstract bool IsValid(TJob job);

    protected abstract int GetRetryCount(TJob job);

    protected abstract void IncrementRetryCount(TJob job);

    protected abstract Guid GetJobId(TJob job);

    protected abstract Task HandleJobAsync(TJob job, CancellationToken cancellationToken);

    protected abstract Task PublishRetryAsync(TJob job, CancellationToken cancellationToken);

    protected abstract Task PublishDeadLetterAsync(TJob job, CancellationToken cancellationToken);

    /// <summary>Called after retries are exhausted (message already marked failed / DLQ'd as needed).</summary>
    protected virtual Task OnRetriesExhaustedAsync(TJob job, CancellationToken cancellationToken) =>
        Task.CompletedTask;

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
                    _logger.LogWarning("Queue {Queue} does not exist yet; retrying in 5s", _queueName);
                }
                else
                {
                    _logger.LogError(ex, "{Queue} RabbitMQ consumer loop faulted; reconnecting in 5s", _queueName);
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
        await channel.BasicQosAsync(0, _prefetchCount, false, stoppingToken);

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
                _logger.LogError(ex, "Unhandled error processing {Queue} delivery {DeliveryTag}", _queueName, ea.DeliveryTag);
                try
                {
                    await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: CancellationToken.None);
                }
                catch (Exception nackEx)
                {
                    _logger.LogWarning(nackEx, "Failed to nack {Queue} delivery {DeliveryTag}", _queueName, ea.DeliveryTag);
                }
            }
        };

        consumer.ShutdownAsync += (_, args) =>
        {
            _logger.LogWarning("{Queue} consumer shut down: {ReplyText}", _queueName, args.ReplyText);
            tcs.TrySetResult();
            return Task.CompletedTask;
        };

        await channel.BasicConsumeAsync(
            queue: _queueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation("Consuming queue {Queue}", _queueName);
        await tcs.Task;
    }

    private async Task HandleDeliveryAsync(IChannel channel, BasicDeliverEventArgs ea, CancellationToken stoppingToken)
    {
        TJob? job;
        try
        {
            job = JsonSerializer.Deserialize<TJob>(ea.Body.Span, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Invalid {Queue} JSON; sending to DLQ", _queueName);
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: CancellationToken.None);
            return;
        }

        if (job is null || !IsValid(job))
        {
            _logger.LogError("Invalid {Queue} body; sending to DLQ", _queueName);
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: CancellationToken.None);
            return;
        }

        try
        {
            await HandleJobAsync(job, stoppingToken);
            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: CancellationToken.None);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true, cancellationToken: CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            var attempt = GetRetryCount(job) + 1;
            _logger.LogWarning(ex, "{Queue} job {JobId} failed (attempt {Attempt})", _queueName, GetJobId(job), attempt);

            if (attempt < _options.MaxAttempts)
            {
                IncrementRetryCount(job);
                await PublishRetryAsync(job, CancellationToken.None);
                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: CancellationToken.None);
            }
            else
            {
                await OnRetriesExhaustedAsync(job, CancellationToken.None);
                await PublishDeadLetterAsync(job, CancellationToken.None);
                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: CancellationToken.None);
                _logger.LogError("{Queue} job {JobId} exhausted retries; published to DLX", _queueName, GetJobId(job));
            }
        }
    }
}
