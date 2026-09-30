namespace SteamSync.Worker.Infrastructure.Configuration;

public class RabbitMqOptions
{
    public const string SectionName = "RabbitMQ";

    public string Host { get; set; } = "localhost";
    public ushort Port { get; set; } = 5672;
    public string Username { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";

    /// <summary>Optional amqp URI (overrides Host/Port/credentials when set).</summary>
    public string? Url { get; set; }
}

public class RateLimiterOptions
{
    public const string SectionName = "RateLimiter";

    /// <summary>Max concurrent Steam API gated operations (mirrors SyncWorker:Concurrency).</summary>
    public int MaxConcurrent { get; set; } = 3;
}
