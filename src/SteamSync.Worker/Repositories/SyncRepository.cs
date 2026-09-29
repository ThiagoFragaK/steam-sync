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
        row.SyncProgressPercent = 100;
        row.LastError = null;
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

    public async Task MarkFailedAsync(int userId, string error, CancellationToken cancellationToken = default)
    {
        var row = await GetOrCreateAsync(userId, cancellationToken);
        row.Status = SyncStatus.Failed;
        row.LastError = error.Length > 2000 ? error[..2000] : error;
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

        var status = await _db.UserSyncStatuses.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);

        // Activate once a full sync has completed successfully.
        if (status is null || status.Status != SyncStatus.Complete || status.LastFullSync is null)
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
