using SteamSync.Shared.Messages;
using SteamSync.Worker.Application.Steam.Interfaces;

namespace SteamSync.Worker.Application.Steam;

public class GamesListSyncHandler
{
    private readonly ISyncJobPublisher _publisher;
    private readonly ILogger<GamesListSyncHandler> _logger;

    public GamesListSyncHandler(ISyncJobPublisher publisher, ILogger<GamesListSyncHandler> logger)
    {
        _publisher = publisher;
        _logger = logger;
    }

    public async Task HandleAsync(GamesListSyncJob job, CancellationToken cancellationToken = default)
    {
        var appIds = (job.AppIds ?? [])
            .Where(id => id > 0)
            .Distinct()
            .ToArray();

        foreach (var appId in appIds)
        {
            await _publisher.PublishUserAchievementsSyncAsync(
                new UserAchievementsSyncJob
                {
                    JobId = Guid.NewGuid(),
                    UserId = job.UserId,
                    SteamId = job.SteamId,
                    AppId = appId,
                    Priority = string.IsNullOrWhiteSpace(job.Priority)
                        ? SyncJobPriorities.Low
                        : job.Priority
                },
                cancellationToken);
        }

        _logger.LogInformation(
            "GamesListSync for user {UserId}: fanned out {Count} UserAchievementsSync jobs",
            job.UserId,
            appIds.Length);
    }
}
