using Microsoft.EntityFrameworkCore;
using SteamSync.Shared;
using SteamSync.Worker.Data;
using SteamSync.Worker.Entities;
using SteamSync.Worker.Enums;

namespace SteamSync.Worker.Repositories;

public class SyncRepository : ISyncRepository
{
    private readonly WorkerDbContext _db;
    private readonly ILogger<SyncRepository> _logger;

    public SyncRepository(WorkerDbContext db, ILogger<SyncRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<UserSyncStatus> UpsertPendingAsync(int userId, Guid jobId, CancellationToken cancellationToken = default)
    {
        var row = await _db.UserSyncStatuses.FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);
        if (row is null)
        {
            row = new UserSyncStatus { UserId = userId };
            _db.UserSyncStatuses.Add(row);
        }

        row.Status = SyncStatus.Pending;
        row.LastJobId = jobId;
        row.LastError = null;
        row.SyncProgressPercent = 0;
        row.LockedUntil = DateTimeOffset.UtcNow.AddMinutes(30);
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return row;
    }

    public async Task MarkSyncingAsync(int userId, Guid jobId, int totalGames, CancellationToken cancellationToken = default)
    {
        var row = await GetOrCreateAsync(userId, cancellationToken);
        row.Status = SyncStatus.Syncing;
        row.LastJobId = jobId;
        row.TotalGamesCount = Math.Max(0, totalGames);
        row.GamesSyncedCount = 0;
        row.SyncProgressPercent = 0;
        row.LastError = null;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateProgressAsync(
        int userId,
        int gamesSynced,
        int totalGames,
        decimal progressPercent,
        CancellationToken cancellationToken = default)
    {
        var row = await GetOrCreateAsync(userId, cancellationToken);
        row.GamesSyncedCount = gamesSynced;
        row.TotalGamesCount = totalGames;
        row.SyncProgressPercent = Math.Clamp(progressPercent, 0, 100);
        row.Status = SyncStatus.Syncing;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkCompleteAsync(
        int userId,
        bool wasFullSync,
        int gamesSynced,
        int totalGames,
        CancellationToken cancellationToken = default)
    {
        var row = await GetOrCreateAsync(userId, cancellationToken);
        row.Status = SyncStatus.Complete;
        row.GamesSyncedCount = gamesSynced;
        row.TotalGamesCount = totalGames;
        row.SyncProgressPercent = totalGames == 0
            ? 100
            : Math.Clamp(Math.Round((decimal)gamesSynced / totalGames * 100, 2), 0, 100);
        if (wasFullSync)
        {
            row.SyncProgressPercent = 100;
        }

        row.LastError = null;
        row.LockedUntil = null;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        if (wasFullSync)
        {
            row.LastFullSync = DateTime.UtcNow;
        }
        else
        {
            row.LastPartialSync = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkPartialAsync(
        int userId,
        int gamesSynced,
        int totalGames,
        CancellationToken cancellationToken = default)
    {
        var row = await GetOrCreateAsync(userId, cancellationToken);
        row.Status = SyncStatus.Partial;
        row.GamesSyncedCount = gamesSynced;
        row.TotalGamesCount = totalGames;
        row.SyncProgressPercent = totalGames == 0
            ? 100
            : Math.Clamp(Math.Round((decimal)gamesSynced / totalGames * 100, 2), 0, 100);
        row.LastPartialSync = DateTime.UtcNow;
        row.LastError = null;
        row.LockedUntil = null;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(int userId, string error, CancellationToken cancellationToken = default)
    {
        var row = await GetOrCreateAsync(userId, cancellationToken);
        row.Status = SyncStatus.Failed;
        row.LastError = error.Length > 2000 ? error[..2000] : error;
        row.LockedUntil = null;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public Task<UserSyncStatus?> GetAsync(int userId, CancellationToken cancellationToken = default) =>
        _db.UserSyncStatuses.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);

    public async Task TryActivateProvisioningUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null || user.Status != (int)StatusEnum.Provisioning)
        {
            return;
        }

        // New users are Active immediately; activate any legacy Provisioning accounts once Partial or Complete.
        var status = await _db.UserSyncStatuses.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);

        if (status is null
            || (status.Status != SyncStatus.Complete
                && status.Status != SyncStatus.Partial
                && status.LastPartialSync is null
                && status.LastFullSync is null))
        {
            return;
        }

        user.Status = (int)StatusEnum.Active;
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("User {UserId} activated after provisioning sync", userId);
    }

    private async Task<UserSyncStatus> GetOrCreateAsync(int userId, CancellationToken cancellationToken)
    {
        var row = await _db.UserSyncStatuses.FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);
        if (row is not null)
        {
            return row;
        }

        row = new UserSyncStatus { UserId = userId };
        _db.UserSyncStatuses.Add(row);
        await _db.SaveChangesAsync(cancellationToken);
        return row;
    }
}
