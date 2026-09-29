namespace SteamSync.Worker.Support;

public static class RecentGamesCacheKeys
{
    public static readonly TimeSpan AbsoluteExpiration = TimeSpan.FromMinutes(10);

    public static string ForSteamId(string steamId) => $"recent-games:{steamId.Trim()}";
}
