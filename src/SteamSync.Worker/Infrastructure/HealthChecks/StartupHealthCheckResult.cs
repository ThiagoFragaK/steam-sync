namespace SteamSync.Worker.Infrastructure.HealthChecks;

public sealed record StartupHealthCheckResult(bool IsHealthy, string? Error, Exception? Exception)
{
    public static StartupHealthCheckResult Healthy() => new(true, null, null);

    public static StartupHealthCheckResult Unhealthy(string error, Exception? exception = null) =>
        new(false, error, exception);
}
