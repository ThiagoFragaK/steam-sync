using SteamSync.Worker.Infrastructure.HealthChecks;
using SteamSync.Worker.Infrastructure.HealthChecks.Interfaces;
using SteamSync.Worker.Infrastructure.Options;
using SteamSync.Worker.Infrastructure.Startup.Interfaces;
using Microsoft.Extensions.Options;

namespace SteamSync.Worker.Infrastructure.Startup;

/// <summary>
/// Runs health checks in registration order (each with retries), then runs startup tasks only if all
/// checks pass. Returns false when the application should not start.
/// </summary>
public sealed class StartupOrchestrator
{
    private readonly IReadOnlyList<IStartupHealthCheck> _healthChecks;
    private readonly IReadOnlyList<IStartupTask> _startupTasks;
    private readonly StartupHealthCheckOptions _options;
    private readonly ILogger<StartupOrchestrator> _logger;

    public StartupOrchestrator(
        IEnumerable<IStartupHealthCheck> healthChecks,
        IEnumerable<IStartupTask> startupTasks,
        IOptions<StartupHealthCheckOptions> options,
        ILogger<StartupOrchestrator> logger)
    {
        _healthChecks = healthChecks.ToList();
        _startupTasks = startupTasks.ToList();
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> RunAsync(CancellationToken cancellationToken = default)
    {
        foreach (var healthCheck in _healthChecks)
        {
            if (!await RunHealthCheckAsync(healthCheck, cancellationToken))
            {
                return false;
            }
        }

        foreach (var task in _startupTasks)
        {
            try
            {
                await task.ExecuteAsync(cancellationToken);
                _logger.LogInformation("Startup task {Name}: OK", task.Name);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Startup task {Name}: ERROR {Error}", task.Name, ex.Message);
                return false;
            }
        }

        return true;
    }

    private async Task<bool> RunHealthCheckAsync(IStartupHealthCheck healthCheck, CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Max(1, _options.MaxAttempts);
        var delay = TimeSpan.FromSeconds(Math.Max(0, _options.DelaySeconds));
        var timeout = TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds));

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptCts.CancelAfter(timeout);

            StartupHealthCheckResult result;
            try
            {
                result = await healthCheck.CheckAsync(attemptCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                result = StartupHealthCheckResult.Unhealthy($"Timed out after {timeout.TotalSeconds:0}s.");
            }

            if (result.IsHealthy)
            {
                _logger.LogInformation("Startup check {Name}: OK", healthCheck.Name);
                return true;
            }

            if (attempt == maxAttempts)
            {
                _logger.LogError(
                    result.Exception,
                    "Startup check {Name}: ERROR {Error} (gave up after {Attempts} attempts)",
                    healthCheck.Name,
                    result.Error,
                    maxAttempts);
                return false;
            }

            _logger.LogWarning(
                "Startup check {Name} failed (attempt {Attempt}/{MaxAttempts}): {Error} Retrying in {Delay}s.",
                healthCheck.Name,
                attempt,
                maxAttempts,
                result.Error,
                delay.TotalSeconds);
            await Task.Delay(delay, cancellationToken);
        }

        return false;
    }
}
