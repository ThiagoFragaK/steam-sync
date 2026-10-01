namespace SteamSync.Shared;

/// <summary>Compact sync progress for API/UI responses.</summary>
public class SyncSummaryDto
{
    public string Status { get; set; } = SyncStatus.Pending.ToString();
    public DateTime? LastFullSync { get; set; }
    public DateTime? LastPartialSync { get; set; }
    public int GamesSynced { get; set; }
    public int GamesTotal { get; set; }
    public decimal Percent { get; set; }
    public string? LastError { get; set; }
    public Guid? LastJobId { get; set; }
    public bool Enqueued { get; set; } = true;

    public static SyncSummaryDto Empty { get; } = new();

    public static SyncSummaryDto From(
        SyncStatus status,
        DateTime? lastFullSync,
        DateTime? lastPartialSync,
        int gamesSynced,
        int gamesTotal,
        decimal percent,
        string? lastError = null,
        Guid? lastJobId = null,
        bool enqueued = true) =>
        new()
        {
            Status = status.ToString(),
            LastFullSync = lastFullSync,
            LastPartialSync = lastPartialSync,
            GamesSynced = gamesSynced,
            GamesTotal = gamesTotal,
            Percent = percent,
            LastError = lastError,
            LastJobId = lastJobId,
            Enqueued = enqueued
        };
}
