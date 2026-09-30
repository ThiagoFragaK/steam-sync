namespace SteamSync.Shared;

/// <summary>Sync progress block attached to auth and user-facing data responses.</summary>
public class SyncSummaryDto
{
    /// <summary>Lowercase status label: pending, syncing, complete, failed, partial.</summary>
    public string Status { get; set; } = "pending";

    public DateTime? LastFullSync { get; set; }

    public DateTime? LastPartialSync { get; set; }

    public int GamesSynced { get; set; }

    public int GamesTotal { get; set; }

    public decimal ProgressPercent { get; set; }

    public static SyncSummaryDto From(
        SyncStatus status,
        DateTime? lastFullSync,
        DateTime? lastPartialSync,
        int gamesSynced,
        int gamesTotal,
        decimal progressPercent) =>
        new()
        {
            Status = status.ToString().ToLowerInvariant(),
            LastFullSync = lastFullSync,
            LastPartialSync = lastPartialSync,
            GamesSynced = gamesSynced,
            GamesTotal = gamesTotal,
            ProgressPercent = progressPercent
        };

    public static SyncSummaryDto Empty => new();
}
