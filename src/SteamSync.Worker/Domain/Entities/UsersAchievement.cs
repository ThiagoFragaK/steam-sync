using SteamSync.Worker.Domain.Interfaces;

namespace SteamSync.Worker.Domain.Entities;

public class UsersAchievement : IEntity
{
    public int Id { get; set; }
    public int AchievementId { get; set; }
    public int UserId { get; set; }
    public int GameId { get; set; }
    public DateTime? UnlockDate { get; set; }

    public Achievement Achievement { get; set; } = null!;
    public User User { get; set; } = null!;
    public Game Game { get; set; } = null!;
}
