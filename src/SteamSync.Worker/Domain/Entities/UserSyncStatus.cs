using SteamSync.Shared;

namespace SteamSync.Worker.Domain.Entities;

/// <summary>Tracks per-user sync progress in <c>user_sync_status</c>.</summary>
public class UserSyncStatus
{
    public int UserId { get; set; }
    public DateTime? LastPartialSync { get; set; }
    public DateTime? LastFullSync { get; set; }
    public decimal SyncProgressPercent { get; set; }
    public int GamesSyncedCount { get; set; }
    public int TotalGamesCount { get; set; }
    public SyncStatus Status { get; set; } = SyncStatus.Pending;
    public PipelineStage PipelineStage { get; set; } = PipelineStage.None;
    public string? LastError { get; set; }
    public Guid? LastJobId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public User User { get; set; } = null!;
}
