namespace SteamSync.Shared;

/// <summary>Persisted sync progress for a user in <c>user_sync_status</c>.</summary>
public enum SyncStatus
{
    Pending = 0,
    Syncing = 1,
    Complete = 2,
    Failed = 3
}
