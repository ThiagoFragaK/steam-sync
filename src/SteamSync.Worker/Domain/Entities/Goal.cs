using SteamSync.Worker.Domain.Enums;
using SteamSync.Worker.Domain.Interfaces;

namespace SteamSync.Worker.Domain.Entities;

public class Goal : IEntity
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public int UserId { get; set; }
    public decimal PercentageGoal { get; set; }
    public int AchievementsMissing { get; set; }
    public GoalStatus Status { get; set; } = GoalStatus.Active;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }

    public Game Game { get; set; } = null!;
    public User User { get; set; } = null!;
    public ICollection<GoalAchievement> GoalAchievements { get; set; } = [];
}
