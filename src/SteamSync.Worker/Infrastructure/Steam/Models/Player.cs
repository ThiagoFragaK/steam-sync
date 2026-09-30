namespace SteamSync.Worker.Infrastructure.Steam.Models;

public class Player
{
    public string? SteamId { get; set; }
    public string? PersonaName { get; set; }
    public string? ProfileUrl { get; set; }
    public string? Avatar { get; set; }
    public string? AvatarFull { get; set; }
    public int? CommunityVisibilityState { get; set; }
    public int? PersonaState { get; set; }
}
