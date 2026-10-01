using SteamSync.Shared;
using SteamSync.Worker.Domain.Entities;

namespace SteamSync.Worker.Application.Steam.Interfaces;

public interface ISyncRepository
{
    Task<UserSyncStatus> UpsertPendingAsync(int userId, Guid jobId, CancellationToken cancellationToken = default);

    Task MarkSyncingAsync(int userId, Guid jobId, int totalGames, CancellationToken cancellationToken = default);

    Task StartPipelineWaveAsync(
        int userId,
        Guid jobId,
        PipelineStage stage,
        int totalGames,
        CancellationToken cancellationToken = default);

    Task SetPipelineStageAsync(
        int userId,
        PipelineStage stage,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically increments wave progress. Returns updated counts/stage, or null if status row missing.
    /// </summary>
    Task<WaveProgressSnapshot?> IncrementWaveProgressAsync(
        int userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Advances stage only when current stage matches <paramref name="expectedStage"/>.
    /// Returns true when this caller won the transition (should publish next work).
    /// </summary>
    Task<bool> TryAdvancePipelineStageAsync(
        int userId,
        PipelineStage expectedStage,
        PipelineStage nextStage,
        CancellationToken cancellationToken = default);

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

    Task MarkFailedAsync(int userId, string error, CancellationToken cancellationToken = default);

    /// <summary>Records an error without failing the whole pipeline (wave can continue).</summary>
    Task SetLastErrorAsync(int userId, string error, CancellationToken cancellationToken = default);

    Task<UserSyncStatus?> GetAsync(int userId, CancellationToken cancellationToken = default);

    Task TryActivateProvisioningUserAsync(int userId, CancellationToken cancellationToken = default);

    Task ActivateUserAsync(int userId, CancellationToken cancellationToken = default);
}

public sealed record WaveProgressSnapshot(
    int GamesSyncedCount,
    int TotalGamesCount,
    PipelineStage PipelineStage,
    decimal SyncProgressPercent);
