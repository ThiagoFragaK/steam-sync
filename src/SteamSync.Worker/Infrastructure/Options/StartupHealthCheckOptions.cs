namespace SteamSync.Worker.Infrastructure.Options;

public class StartupHealthCheckOptions
{
    public const string SectionName = "StartupHealthChecks";

    public int MaxAttempts { get; set; } = 5;

    public int DelaySeconds { get; set; } = 5;

    public int TimeoutSeconds { get; set; } = 10;
}
