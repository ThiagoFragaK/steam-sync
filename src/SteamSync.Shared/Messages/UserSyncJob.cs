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

/// <summary>Fan-out chunk of app IDs into per-game achievement sync jobs.</summary>
public class GamesListSyncJob
{
    public Guid JobId { get; set; } = Guid.NewGuid();

    public int UserId { get; set; }

    public string SteamId { get; set; } = string.Empty;

    public int[] AppIds { get; set; } = [];

    /// <summary>One of <see cref="SyncJobPriorities"/> values. Routes fan-out to high/low achievements queues.</summary>
    public string Priority { get; set; } = SyncJobPriorities.Low;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int RetryCount { get; set; }
}

/// <summary>Sync achievements for a single owned game.</summary>
public class UserAchievementsSyncJob
{
    public Guid JobId { get; set; } = Guid.NewGuid();

    public int UserId { get; set; }

    public string SteamId { get; set; } = string.Empty;

    public int AppId { get; set; }

    /// <summary>One of <see cref="SyncJobPriorities"/> values. Routes to high/low achievements queues.</summary>
    public string Priority { get; set; } = SyncJobPriorities.Low;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int RetryCount { get; set; }
}

/// <summary>Full owned-library upsert, then fan-out remaining achievement syncs.</summary>
public class SyncLibraryJob
{
    public Guid JobId { get; set; } = Guid.NewGuid();

    public int UserId { get; set; }

    public string SteamId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int RetryCount { get; set; }
}

public static class SyncJobPriorities
{
    public const string High = "high";
    public const string Low = "low";

    public static bool IsHigh(string? priority) =>
        string.Equals(priority, High, StringComparison.OrdinalIgnoreCase);
}

public static class SyncJobTypes
{
    public const string UserSync = "user_sync";
    public const string FullLibraryResync = "full_library_resync";
    public const string RecentActivityOnly = "recent_activity_only";
    public const string FirstSync = "first_sync";
    public const string GamesListSync = "games_list_sync";
    public const string UserAchievementsSync = "user_achievements_sync";
    public const string SyncLibrary = "sync_library";
}

public static class SyncQueueNames
{
    public const string Jobs = "steam_sync_jobs";
    public const string DeadLetterExchange = "steam_sync_dlx";
    public const string DeadLetterQueue = "steam_sync_jobs_dlq";

    public const string FirstSync = "first_sync";
    public const string FirstSyncDeadLetterExchange = "first_sync_dlx";
    public const string FirstSyncDeadLetterQueue = "first_sync_dlq";

    public const string GamesListSync = "games_list_sync";
    public const string GamesListSyncDeadLetterExchange = "games_list_sync_dlx";
    public const string GamesListSyncDeadLetterQueue = "games_list_sync_dlq";

    public const string UserAchievementsSyncHigh = "user_achievements_sync_high";
    public const string UserAchievementsSyncHighDeadLetterExchange = "user_achievements_sync_high_dlx";
    public const string UserAchievementsSyncHighDeadLetterQueue = "user_achievements_sync_high_dlq";

    public const string UserAchievementsSyncLow = "user_achievements_sync_low";
    public const string UserAchievementsSyncLowDeadLetterExchange = "user_achievements_sync_low_dlx";
    public const string UserAchievementsSyncLowDeadLetterQueue = "user_achievements_sync_low_dlq";

    public const string SyncLibrary = "sync_library";
    public const string SyncLibraryDeadLetterExchange = "sync_library_dlx";
    public const string SyncLibraryDeadLetterQueue = "sync_library_dlq";
}
