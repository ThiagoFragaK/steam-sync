using SteamSync.Worker.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace SteamSync.Worker.Infrastructure.Steam;

public sealed class SteamApiThrottle : IDisposable
{
    private readonly SemaphoreSlim _gate;

    public SteamApiThrottle(IOptions<SyncWorkerOptions> options)
    {
        _gate = new SemaphoreSlim(Math.Max(1, options.Value.Concurrency));
    }

    public Task WaitAsync(CancellationToken cancellationToken) => _gate.WaitAsync(cancellationToken);

    public void Release() => _gate.Release();

    public void Dispose() => _gate.Dispose();
}
