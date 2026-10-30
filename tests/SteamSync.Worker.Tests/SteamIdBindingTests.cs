using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SteamSync.Worker.Application.Goals.Interfaces;
using SteamSync.Worker.Application.Steam;
using SteamSync.Worker.Application.Steam.Interfaces;
using SteamSync.Worker.Domain.Entities;
using SteamSync.Worker.Infrastructure.Options;
using SteamSync.Worker.Infrastructure.Persistence;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace SteamSync.Worker.Tests;

public class SteamIdBindingTests
{
    [Fact]
    public void EnsureSteamIdMatchesUser_rejects_mismatched_ids()
    {
        var user = new User { Id = 1, SteamId = "76561198000000001", Email = "a@b.com", Password = "x" };
        Assert.Throws<InvalidOperationException>(() =>
            SteamSyncService.EnsureSteamIdMatchesUser(user, "76561198000000099"));
    }

    [Fact]
    public async Task SyncLibraryAsync_does_not_call_steam_when_steamId_mismatches()
    {
        var options = new DbContextOptionsBuilder<WorkerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        await using var db = new WorkerDbContext(options);
        db.Users.Add(new User
        {
            Id = 1,
            Email = "a@b.com",
            Password = "x",
            SteamId = "76561198000000001"
        });
        await db.SaveChangesAsync();

        var fetcher = new Mock<IAchievementFetcher>(MockBehavior.Strict);
        var goals = new Mock<IGoalRefreshService>();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new SteamSyncService(
            db,
            fetcher.Object,
            goals.Object,
            cache,
            MsOptions.Create(new SyncWorkerOptions()),
            NullLogger<SteamSyncService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SyncLibraryAsync(1, "76561198000000099"));

        fetcher.VerifyNoOtherCalls();
    }
}
