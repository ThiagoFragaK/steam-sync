namespace SteamSync.Worker.Infrastructure.Steam.Models;

public class PlayerAchievementsResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? GameName { get; set; }
    public List<PlayerAchievement> Achievements { get; set; } = [];
}
