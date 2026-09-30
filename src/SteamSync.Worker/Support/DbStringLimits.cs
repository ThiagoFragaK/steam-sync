namespace SteamSync.Worker.Support;

/// <summary>Max lengths matching EF column configs / Postgres varchar limits.</summary>
public static class DbStringLimits
{
    public const int GameName = 500;
    public const int GameImageUrl = 500;
    public const int GameHeaderImageUrl = 500;
    public const int GameDevelopers = 500;
    public const int GamePublishers = 500;
    public const int AchievementApiName = 500;
    public const int AchievementName = 500;
    public const int AchievementDescription = 1000;
    public const int AchievementImageUrl = 500;

    public static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }

    public static string? TruncateOrNull(string? value, int maxLength)
    {
        if (value is null)
        {
            return null;
        }

        return Truncate(value, maxLength);
    }
}
