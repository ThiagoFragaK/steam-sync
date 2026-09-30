using RabbitMQ.Client;
using SteamSync.Shared.Messages;

namespace SteamSync.Worker.Messaging;

public static class RabbitMqTopology
{
    public static async Task DeclareAsync(IChannel channel, CancellationToken cancellationToken = default)
    {
        await channel.ExchangeDeclareAsync(
            exchange: SyncQueueNames.DeadLetterExchange,
            type: ExchangeType.Fanout,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            queue: SyncQueueNames.DeadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        await channel.QueueBindAsync(
            queue: SyncQueueNames.DeadLetterQueue,
            exchange: SyncQueueNames.DeadLetterExchange,
            routingKey: string.Empty,
            cancellationToken: cancellationToken);

        var args = new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = SyncQueueNames.DeadLetterExchange
        };

        await channel.QueueDeclareAsync(
            queue: SyncQueueNames.Jobs,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: args,
            cancellationToken: cancellationToken);
    }
}
