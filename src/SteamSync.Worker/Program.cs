using Serilog;
using SteamSync.Worker.Extensions;
using SteamSync.Worker.Options;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddSerilog((services, loggerConfiguration) => loggerConfiguration
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console());

    var steamKey = builder.Configuration.GetSection(SteamApiOptions.SectionName).Get<SteamApiOptions>()?.ApiKey;
    if (string.IsNullOrWhiteSpace(steamKey))
    {
        throw new InvalidOperationException(
            "SteamApi:ApiKey must be configured via environment variable SteamApi__ApiKey or appsettings.");
    }

    builder.Services.AddSteamSyncWorker(builder.Configuration);

    var host = builder.Build();
    Log.Information("steam-sync worker starting");
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "steam-sync worker terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}
