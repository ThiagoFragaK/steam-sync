namespace SteamSync.Shared.Messages;

/// <summary>RabbitMQ message for a user Steam sync job.</summary>
public class UserSyncJob
{
    public Guid JobId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// One of <see cref="SyncJobTypes"/> values:
    /// user_sync, full_library_resync, recent_activity_only.
    /// </summary>
    public string JobType { get; set; } = SyncJobTypes.UserSync;

    public UserSyncPayload Payload { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int RetryCount { get; set; }
}

public class UserSyncPayload
{
    public int UserId { get; set; }

    public string SteamId { get; set; } = string.Empty;

    /// <summary>low | medium | high</summary>
    public string Priority { get; set; } = "medium";

    /// <summary>When set with JobType user_sync, syncs a single game's achievements.</summary>
    public int? AppId { get; set; }

    /// <summary>When true (e.g. nightly), also crawl unsynced achievement percentages.</summary>
    public bool IncludeCrawl { get; set; }
}

/// <summary>High-priority first sync after registration (recently played games).</summary>
public class FirstSyncJob
{
    public Guid JobId { get; set; } = Guid.NewGuid();

    public int UserId { get; set; }

    public string SteamId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int RetryCount { get; set; }
}

public static class SyncJobTypes
{
    public const string UserSync = "user_sync";
    public const string FullLibraryResync = "full_library_resync";
    public const string RecentActivityOnly = "recent_activity_only";
    public const string FirstSync = "first_sync";
}

public static class SyncQueueNames
{
    public const string Jobs = "steam_sync_jobs";
    public const string DeadLetterExchange = "steam_sync_dlx";
    public const string DeadLetterQueue = "steam_sync_jobs_dlq";

    public const string FirstSync = "first_sync";
    public const string FirstSyncDeadLetterExchange = "first_sync_dlx";
    public const string FirstSyncDeadLetterQueue = "first_sync_dlq";
}
