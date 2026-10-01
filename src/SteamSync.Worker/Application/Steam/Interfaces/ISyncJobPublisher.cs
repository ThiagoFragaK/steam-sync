using SteamSync.Shared.Messages;

namespace SteamSync.Worker.Application.Steam.Interfaces;

public interface ISyncJobPublisher
{
    Task PublishAsync(UserSyncJob job, CancellationToken cancellationToken = default);

    Task PublishDeadLetterAsync(UserSyncJob job, CancellationToken cancellationToken = default);

    Task PublishFirstSyncAsync(FirstSyncJob job, CancellationToken cancellationToken = default);

    Task PublishFirstSyncDeadLetterAsync(FirstSyncJob job, CancellationToken cancellationToken = default);

    Task PublishGamesListSyncAsync(GamesListSyncJob job, CancellationToken cancellationToken = default);

    Task PublishGamesListSyncDeadLetterAsync(GamesListSyncJob job, CancellationToken cancellationToken = default);

    Task PublishUserAchievementsSyncAsync(UserAchievementsSyncJob job, CancellationToken cancellationToken = default);

    Task PublishUserAchievementsSyncDeadLetterAsync(UserAchievementsSyncJob job, CancellationToken cancellationToken = default);

    Task PublishSyncLibraryAsync(SyncLibraryJob job, CancellationToken cancellationToken = default);

    Task PublishSyncLibraryDeadLetterAsync(SyncLibraryJob job, CancellationToken cancellationToken = default);
}
