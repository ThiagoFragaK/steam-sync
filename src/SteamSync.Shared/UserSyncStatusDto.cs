namespace SteamSync.Shared;

/// <summary>API-facing sync status snapshot.</summary>
public class UserSyncStatusDto
{
    public int UserId { get; set; }
    public DateTime? LastPartialSync { get; set; }
    public DateTime? LastFullSync { get; set; }
    public decimal SyncProgressPercent { get; set; }
    public int GamesSyncedCount { get; set; }
    public int TotalGamesCount { get; set; }
    public SyncStatus Status { get; set; }
    public string? LastError { get; set; }
    public Guid? LastJobId { get; set; }

    /// <summary>Canonical sync block for frontend progress UI.</summary>
    public SyncSummaryDto Sync => SyncSummaryDto.From(
        Status,
        LastFullSync,
        LastPartialSync,
        GamesSyncedCount,
        TotalGamesCount,
        SyncProgressPercent);
}
