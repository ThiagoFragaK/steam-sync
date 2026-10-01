namespace SteamSync.Shared;

/// <summary>Registration sync pipeline stage stored on <c>user_sync_status</c>.</summary>
public enum PipelineStage
{
    None = 0,
    RecentAchievements = 1,
    FullLibrary = 2,
    FullAchievements = 3,
    Done = 4
}
