using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace SteamSync.Worker.Metrics;

public static class SyncMetrics
{
    public const string MeterName = "SteamSync.Worker";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    public static readonly Counter<long> JobsProcessedTotal =
        Meter.CreateCounter<long>("jobs_processed_total", description: "Successfully processed sync jobs");

    public static readonly Counter<long> JobsFailedTotal =
        Meter.CreateCounter<long>("jobs_failed_total", description: "Failed sync jobs");

    public static readonly Histogram<double> SyncDurationSeconds =
        Meter.CreateHistogram<double>("sync_duration_seconds", unit: "s", description: "Sync job duration");

    public static ActivitySource ActivitySource { get; } = new("SteamSync.Worker");
}
