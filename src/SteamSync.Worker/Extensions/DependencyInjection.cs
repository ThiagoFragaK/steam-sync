using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using SteamSync.Worker.Configuration;
using SteamSync.Worker.Data;
using SteamSync.Worker.MessageHandlers;
using SteamSync.Worker.Messaging;
using SteamSync.Worker.Metrics;
using SteamSync.Worker.Options;
using SteamSync.Worker.Repositories;
using SteamSync.Worker.Services;
using SteamSync.Worker.Services.Interfaces;
using SteamSync.Worker.Workers;

namespace SteamSync.Worker.Extensions;

public static class DependencyInjection
{
    public static IServiceCollection AddSteamSyncWorker(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SteamApiOptions>(configuration.GetSection(SteamApiOptions.SectionName));
        services.Configure<SyncWorkerOptions>(configuration.GetSection(SyncWorkerOptions.SectionName));
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));
        services.Configure<RateLimiterOptions>(configuration.GetSection(RateLimiterOptions.SectionName));

        var connectionString = configuration.GetConnectionString("Postgres")
            ?? configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres (or DefaultConnection) is required.");

        services.AddDbContext<WorkerDbContext>(options => options.UseNpgsql(connectionString));
        services.AddMemoryCache();
        services.AddSingleton<SteamApiThrottle>();
        services.AddScoped<ISteamSyncService, SteamSyncService>();
        services.AddScoped<ISyncRepository, SyncRepository>();
        services.AddScoped<UserSyncMessageHandler>();

        services.AddHttpClient<IAchievementFetcher, AchievementFetcher>()
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 3;
                options.Retry.Delay = TimeSpan.FromSeconds(1);
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(2);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
            });

        services.AddSingleton<IRabbitMqConnectionFactory, RabbitMqConnectionFactory>();
        services.AddSingleton<ISyncJobPublisher, SyncJobPublisher>();
        services.AddHostedService<UserSyncJobConsumerHostedService>();
        services.AddHostedService<NightlyMaintenanceWorker>();

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService("steam-sync-worker"))
            .WithMetrics(m => m
                .AddMeter(SyncMetrics.MeterName)
                .AddHttpClientInstrumentation()
                .AddOtlpExporter());

        return services;
    }
}
