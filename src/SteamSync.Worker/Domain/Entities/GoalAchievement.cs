using SteamSync.Worker.Domain.Interfaces;

namespace SteamSync.Worker.Domain.Entities;

public class GoalAchievement : IEntity
{
    public int Id { get; set; }
    public int AchievementId { get; set; }
    public int GoalId { get; set; }
    public GoalAchievementStatus Status { get; set; } = GoalAchievementStatus.Pending;

    public Achievement Achievement { get; set; } = null!;
    public Goal Goal { get; set; } = null!;
}

public enum GoalAchievementStatus
{
    Pending = 0,
    Completed = 1
}
