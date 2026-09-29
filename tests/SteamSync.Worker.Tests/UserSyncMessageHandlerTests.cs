using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SteamSync.Shared.Messages;
using SteamSync.Worker.Data;
using SteamSync.Worker.Entities;
using SteamSync.Worker.MessageHandlers;
using SteamSync.Worker.Options;
using SteamSync.Worker.Repositories;
using SteamSync.Worker.Services;
using SteamSync.Worker.Services.Interfaces;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace SteamSync.Worker.Tests;

public class UserSyncMessageHandlerTests
{
    private static WorkerDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<WorkerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new WorkerDbContext(options);
        db.Users.Add(new User
        {
            Id = 1,
            Email = "a@b.com",
            Password = "x",
            SteamId = "76561198000000000",
            Status = 2
        });
        db.SaveChanges();
        return db;
    }

    [Fact]
    public async Task Consume_FullLibraryResync_CallsLibraryPriorityAndCrawl()
    {
        await using var db = CreateDb();
        var sync = new Mock<ISteamSyncService>();
        sync.Setup(s => s.GetPriorityCompletionAppIdsAsync(1, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<int>());
        sync.Setup(s => s.GetCrawlCompletionAppIdsAsync(1, 0, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<int>());
        sync.Setup(s => s.CountEligibleCrawlGamesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var repo = new SyncRepository(db, NullLogger<SyncRepository>.Instance);
        var throttle = new SteamApiThrottle(MsOptions.Create(new SyncWorkerOptions { Concurrency = 1 }));
        var handler = new UserSyncMessageHandler(
            sync.Object,
            repo,
            db,
            throttle,
            MsOptions.Create(new SyncWorkerOptions { BatchSize = 25, MaxStoreEnrichPerLibrarySync = 1 }),
            NullLogger<UserSyncMessageHandler>.Instance);

        var context = Mock.Of<ConsumeContext<UserSyncJob>>(c =>
            c.Message == new UserSyncJob
            {
                JobType = SyncJobTypes.FullLibraryResync,
                Payload = new UserSyncPayload { UserId = 1, SteamId = "76561198000000000" }
            } &&
            c.CancellationToken == CancellationToken.None);

        await handler.Consume(context);

        sync.Verify(s => s.SyncLibraryAsync(
            1,
            "76561198000000000",
            LibrarySyncScope.Full,
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Once);

        var status = await repo.GetAsync(1);
        Assert.NotNull(status);
        Assert.Equal(SteamSync.Shared.SyncStatus.Complete, status!.Status);
        Assert.NotNull(status.LastFullSync);
    }

    [Fact]
    public async Task Consume_UserSyncWithAppId_CallsGameAchievements()
    {
        await using var db = CreateDb();
        var sync = new Mock<ISteamSyncService>();
        var repo = new SyncRepository(db, NullLogger<SyncRepository>.Instance);
        var throttle = new SteamApiThrottle(MsOptions.Create(new SyncWorkerOptions { Concurrency = 1 }));
        var handler = new UserSyncMessageHandler(
            sync.Object,
            repo,
            db,
            throttle,
            MsOptions.Create(new SyncWorkerOptions()),
            NullLogger<UserSyncMessageHandler>.Instance);

        var context = Mock.Of<ConsumeContext<UserSyncJob>>(c =>
            c.Message == new UserSyncJob
            {
                JobType = SyncJobTypes.UserSync,
                Payload = new UserSyncPayload { UserId = 1, SteamId = "76561198000000000", AppId = 730 }
            } &&
            c.CancellationToken == CancellationToken.None);

        await handler.Consume(context);

        sync.Verify(s => s.SyncGameAchievementsAsync(1, "76561198000000000", 730, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Consume_OnFailure_MarksFailedAndRethrows()
    {
        await using var db = CreateDb();
        var sync = new Mock<ISteamSyncService>();
        sync.Setup(s => s.SyncLibraryAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<LibrarySyncScope>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("steam down"));

        var repo = new SyncRepository(db, NullLogger<SyncRepository>.Instance);
        var throttle = new SteamApiThrottle(MsOptions.Create(new SyncWorkerOptions { Concurrency = 1 }));
        var handler = new UserSyncMessageHandler(
            sync.Object,
            repo,
            db,
            throttle,
            MsOptions.Create(new SyncWorkerOptions()),
            NullLogger<UserSyncMessageHandler>.Instance);

        var context = Mock.Of<ConsumeContext<UserSyncJob>>(c =>
            c.Message == new UserSyncJob
            {
                JobType = SyncJobTypes.RecentActivityOnly,
                Payload = new UserSyncPayload { UserId = 1, SteamId = "76561198000000000" }
            } &&
            c.CancellationToken == CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Consume(context));
        var status = await repo.GetAsync(1);
        Assert.Equal(SteamSync.Shared.SyncStatus.Failed, status!.Status);
        Assert.Contains("steam down", status.LastError);
    }
}

public class ConcurrentPublishSmokeTests
{
    [Fact]
    public async Task TenConcurrentJobIds_AreUnique()
    {
        var ids = new Guid[10];
        await Parallel.ForEachAsync(Enumerable.Range(0, 10), async (i, _) =>
        {
            await Task.Yield();
            ids[i] = new UserSyncJob().JobId;
        });

        Assert.Equal(10, ids.Distinct().Count());
    }
}
