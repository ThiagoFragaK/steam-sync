namespace SteamSync.Worker.Infrastructure.Options;

public class SteamApiOptions
{
    public const string SectionName = "SteamApi";

    public string ApiKey { get; set; } = string.Empty;
}
