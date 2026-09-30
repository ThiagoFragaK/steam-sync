namespace SteamSync.Worker.Infrastructure.Options;

public class SyncWorkerOptions
{
    public const string SectionName = "SyncWorker";

    /// <summary>AppRole: Api | Worker | All</summary>
    public string AppRole { get; set; } = "All";

    public int Concurrency { get; set; } = 3;

    public int BatchSize { get; set; } = 25;

    public int SchemaTtlDays { get; set; } = 14;

    public string NightlyCron { get; set; } = "0 0 * * *";

    public int MaxAttempts { get; set; } = 5;

    public int PollIntervalSeconds { get; set; } = 2;

    /// <summary>Max Steam Store appdetails calls during a single library sync job.</summary>
    public int MaxStoreEnrichPerLibrarySync { get; set; } = 5;
}
