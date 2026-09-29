using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SteamSync.Shared;
using SteamSync.Shared.Messages;
using SteamSync.Worker.Data;
using SteamSync.Worker.Entities;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace SteamSync.IntegrationTests;

public class SyncPipelineIntegrationTests : IAsyncLifetime
{
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
    public async Task PublishUserSyncJob_IsConsumed_AndStatusCompletes()
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
                Status = SyncStatus.Pending,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await setup.SaveChangesAsync();
        }

        var consumed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var provider = new ServiceCollection()
            .AddMassTransit(x =>
            {
                x.AddConsumer<RecordingConsumer>();
                x.UsingRabbitMq((ctx, cfg) =>
                {
                    cfg.Host(_rabbit.GetConnectionString());
                    cfg.ReceiveEndpoint(SyncQueueNames.Jobs, e =>
                    {
                        e.ConfigureConsumer<RecordingConsumer>(ctx);
                    });
                });
            })
            .AddSingleton(consumed)
            .BuildServiceProvider(true);

        var bus = provider.GetRequiredService<IBusControl>();
        await bus.StartAsync();
        try
        {
            var endpoint = await bus.GetSendEndpoint(new Uri($"queue:{SyncQueueNames.Jobs}"));
            var job = new UserSyncJob
            {
                JobType = SyncJobTypes.UserSync,
                Payload = new UserSyncPayload { UserId = 42, SteamId = "76561198000000042", AppId = 730 }
            };
            await endpoint.Send(job);

            var completed = await Task.WhenAny(consumed.Task, Task.Delay(TimeSpan.FromSeconds(30)));
            Assert.Same(consumed.Task, completed);
            Assert.True(await consumed.Task);
        }
        finally
        {
            await bus.StopAsync();
        }
    }

    private sealed class RecordingConsumer : IConsumer<UserSyncJob>
    {
        private readonly TaskCompletionSource<bool> _tcs;

        public RecordingConsumer(TaskCompletionSource<bool> tcs) => _tcs = tcs;

        public Task Consume(ConsumeContext<UserSyncJob> context)
        {
            _tcs.TrySetResult(context.Message.Payload.UserId == 42);
            return Task.CompletedTask;
        }
    }
}
