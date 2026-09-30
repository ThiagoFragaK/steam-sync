namespace SteamSync.Worker.Infrastructure.Steam.Models;

public class OwnedGame
{
    public int AppId { get; set; }
    public string? Name { get; set; }
    public string? ImgIconUrl { get; set; }
    public int PlaytimeForeverMinutes { get; set; }
    public long? LastPlayedUnix { get; set; }
    public bool HasCommunityVisibleStats { get; set; }
}
