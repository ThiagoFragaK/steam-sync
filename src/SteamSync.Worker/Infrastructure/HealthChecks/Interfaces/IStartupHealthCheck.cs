namespace SteamSync.Worker.Infrastructure.HealthChecks.Interfaces;

/// <summary>A dependency check that must pass before the worker starts consuming.</summary>
public interface IStartupHealthCheck
{
    string Name { get; }

    Task<StartupHealthCheckResult> CheckAsync(CancellationToken cancellationToken = default);
}
