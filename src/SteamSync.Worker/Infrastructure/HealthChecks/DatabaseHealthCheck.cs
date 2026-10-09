using Microsoft.EntityFrameworkCore;
using SteamSync.Worker.Infrastructure.HealthChecks.Interfaces;
using SteamSync.Worker.Infrastructure.Persistence;

namespace SteamSync.Worker.Infrastructure.HealthChecks;

public sealed class DatabaseHealthCheck : IStartupHealthCheck
{
    private readonly IServiceScopeFactory _scopeFactory;

    public DatabaseHealthCheck(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public string Name => "Database";

    public async Task<StartupHealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();

        try
        {
            // OpenConnection (not CanConnect) so the provider's exception surfaces with the actual cause.
            await db.Database.OpenConnectionAsync(cancellationToken);
            await db.Database.CloseConnectionAsync();
            return StartupHealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return StartupHealthCheckResult.Unhealthy(
                $"{ex.Message} Check ConnectionStrings__Postgres Host, Port, Database, Username, and Password.",
                ex);
        }
    }
}
