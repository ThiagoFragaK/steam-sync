using RabbitMQ.Client;
using SteamSync.Shared.Messages;

namespace SteamSync.IntegrationTests;

/// <summary>
/// Test-only queue topology so the integration test has queues to consume from.
/// The worker itself never declares queues.
/// </summary>
public static class TestTopology
{
    public static async Task DeclareAsync(IChannel channel, CancellationToken cancellationToken = default)
    {
        await DeclareJobsTopologyAsync(channel, cancellationToken);
        await DeclareFirstSyncTopologyAsync(channel, cancellationToken);
        await DeclareGamesListSyncTopologyAsync(channel, cancellationToken);
        await DeclareUserAchievementsSyncHighTopologyAsync(channel, cancellationToken);
        await DeclareUserAchievementsSyncLowTopologyAsync(channel, cancellationToken);
        await DeclareSyncLibraryTopologyAsync(channel, cancellationToken);
    }

    public static async Task DeclareJobsTopologyAsync(IChannel channel, CancellationToken cancellationToken = default) =>
        await DeclareQueuePairAsync(
            channel,
            SyncQueueNames.Jobs,
            SyncQueueNames.DeadLetterExchange,
            SyncQueueNames.DeadLetterQueue,
            cancellationToken);

    public static async Task DeclareFirstSyncTopologyAsync(IChannel channel, CancellationToken cancellationToken = default) =>
        await DeclareQueuePairAsync(
            channel,
            SyncQueueNames.FirstSync,
            SyncQueueNames.FirstSyncDeadLetterExchange,
            SyncQueueNames.FirstSyncDeadLetterQueue,
            cancellationToken);

    public static async Task DeclareGamesListSyncTopologyAsync(IChannel channel, CancellationToken cancellationToken = default) =>
        await DeclareQueuePairAsync(
            channel,
            SyncQueueNames.GamesListSync,
            SyncQueueNames.GamesListSyncDeadLetterExchange,
            SyncQueueNames.GamesListSyncDeadLetterQueue,
            cancellationToken);

    public static async Task DeclareUserAchievementsSyncHighTopologyAsync(
        IChannel channel,
        CancellationToken cancellationToken = default) =>
        await DeclareQueuePairAsync(
            channel,
            SyncQueueNames.UserAchievementsSyncHigh,
            SyncQueueNames.UserAchievementsSyncHighDeadLetterExchange,
            SyncQueueNames.UserAchievementsSyncHighDeadLetterQueue,
            cancellationToken);

    public static async Task DeclareUserAchievementsSyncLowTopologyAsync(
        IChannel channel,
        CancellationToken cancellationToken = default) =>
        await DeclareQueuePairAsync(
            channel,
            SyncQueueNames.UserAchievementsSyncLow,
            SyncQueueNames.UserAchievementsSyncLowDeadLetterExchange,
            SyncQueueNames.UserAchievementsSyncLowDeadLetterQueue,
            cancellationToken);

    public static async Task DeclareSyncLibraryTopologyAsync(IChannel channel, CancellationToken cancellationToken = default) =>
        await DeclareQueuePairAsync(
            channel,
            SyncQueueNames.SyncLibrary,
            SyncQueueNames.SyncLibraryDeadLetterExchange,
            SyncQueueNames.SyncLibraryDeadLetterQueue,
            cancellationToken);

    private static async Task DeclareQueuePairAsync(
        IChannel channel,
        string queue,
        string deadLetterExchange,
        string deadLetterQueue,
        CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(
            exchange: deadLetterExchange,
            type: ExchangeType.Fanout,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            queue: deadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        await channel.QueueBindAsync(
            queue: deadLetterQueue,
            exchange: deadLetterExchange,
            routingKey: string.Empty,
            cancellationToken: cancellationToken);

        var args = new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = deadLetterExchange
        };

        await channel.QueueDeclareAsync(
            queue: queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: args,
            cancellationToken: cancellationToken);
    }
}
