namespace SteamSync.Worker.Entities;

public class UsersGame : IEntity
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public int UserId { get; set; }
    public decimal AchievementsPercentage { get; set; }
    public int PlaytimeMinutes { get; set; }
    public long? LastPlayedUnix { get; set; }
    public DateTimeOffset? AchievementsSyncedAt { get; set; }
    public bool NeedsAchievementRefresh { get; set; }
    /// <summary>True when Steam refused achievements for this game (e.g. 403); excluded from crawl.</summary>
    public bool AchievementSyncUnavailable { get; set; }

    public Game Game { get; set; } = null!;
    public User User { get; set; } = null!;
}
