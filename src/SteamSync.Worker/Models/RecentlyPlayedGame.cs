namespace SteamSync.Worker.Models;

public class RecentlyPlayedGame
{
    public int AppId { get; set; }
    public string? Name { get; set; }
    public int Playtime2WeeksMinutes { get; set; }
    public int PlaytimeForeverMinutes { get; set; }
    public string? ImgIconUrl { get; set; }
}
