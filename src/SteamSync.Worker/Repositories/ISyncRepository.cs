using SteamSync.Shared;
using SteamSync.Worker.Entities;

namespace SteamSync.Worker.Repositories;

public interface ISyncRepository
{
    Task<UserSyncStatus> UpsertPendingAsync(int userId, Guid jobId, CancellationToken cancellationToken = default);

    Task MarkSyncingAsync(int userId, Guid jobId, int totalGames, CancellationToken cancellationToken = default);

    Task UpdateProgressAsync(
        int userId,
        int gamesSynced,
        int totalGames,
        decimal progressPercent,
        CancellationToken cancellationToken = default);

    Task MarkCompleteAsync(
        int userId,
        bool wasFullSync,
        int gamesSynced,
        int totalGames,
        CancellationToken cancellationToken = default);

    /// <summary>Marks initial/priority pass as Partial (incomplete library crawl).</summary>
    Task MarkPartialAsync(
        int userId,
        int gamesSynced,
        int totalGames,
        CancellationToken cancellationToken = default);

    Task MarkFailedAsync(int userId, string error, CancellationToken cancellationToken = default);

    Task<UserSyncStatus?> GetAsync(int userId, CancellationToken cancellationToken = default);

    Task TryActivateProvisioningUserAsync(int userId, CancellationToken cancellationToken = default);
}
