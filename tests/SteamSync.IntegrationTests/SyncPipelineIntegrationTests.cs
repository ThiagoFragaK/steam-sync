using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using SteamSync.Shared.Messages;
using SteamSync.Worker.Data;
using SteamSync.Worker.Entities;
using SteamSync.Worker.Messaging;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace SteamSync.IntegrationTests;

public class SyncPipelineIntegrationTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("achievhub")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:3.13-management-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbit.StartAsync());
    }

    public async Task DisposeAsync()
    {
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _rabbit.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task PublishUserSyncJob_IsConsumed()
    {
        var options = new DbContextOptionsBuilder<WorkerDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        await using (var setup = new WorkerDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Users.Add(new User
            {
                Id = 42,
                Email = "integration@test.com",
                Password = "hash",
                SteamId = "76561198000000042",
                Status = 1
            });
            setup.UserSyncStatuses.Add(new UserSyncStatus
            {
                UserId = 42,
                Status = SteamSync.Shared.SyncStatus.Pending,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await setup.SaveChangesAsync();
        }

        var factory = new ConnectionFactory { Uri = new Uri(_rabbit.GetConnectionString()) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await TestTopology.DeclareAsync(channel);

        var consumed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            try
            {
                var job = JsonSerializer.Deserialize<UserSyncJob>(ea.Body.Span, JsonOptions);
                consumed.TrySetResult(job?.Payload.UserId == 42);
                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                consumed.TrySetException(ex);
            }
        };

        await channel.BasicConsumeAsync(SyncQueueNames.Jobs, autoAck: false, consumer);

        var job = new UserSyncJob
        {
            JobType = SyncJobTypes.UserSync,
            Payload = new UserSyncPayload { UserId = 42, SteamId = "76561198000000042", AppId = 730 }
        };
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(job, JsonOptions));
        var props = new BasicProperties { ContentType = "application/json", DeliveryMode = DeliveryModes.Persistent };
        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: SyncQueueNames.Jobs,
            mandatory: false,
            basicProperties: props,
            body: body);

        var completed = await Task.WhenAny(consumed.Task, Task.Delay(TimeSpan.FromSeconds(30)));
        Assert.Same(consumed.Task, completed);
        Assert.True(await consumed.Task);
    }
}
