namespace SteamSync.Worker.Infrastructure.Steam.Models;

public class PlayerAchievement
{
    public string? ApiName { get; set; }
    public int Achieved { get; set; }
    public long UnlockTimeUnix { get; set; }
}
