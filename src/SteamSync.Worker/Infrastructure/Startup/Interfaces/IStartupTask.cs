namespace SteamSync.Worker.Infrastructure.Startup.Interfaces;

/// <summary>
/// A startup step that runs only after every <c>IStartupHealthCheck</c> has passed.
/// None are registered in the worker: it never declares queues.
/// </summary>
public interface IStartupTask
{
    string Name { get; }

    Task ExecuteAsync(CancellationToken cancellationToken = default);
}
