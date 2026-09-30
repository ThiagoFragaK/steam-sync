using SteamSync.Shared.Messages;

namespace SteamSync.Worker.Messaging;

public interface ISyncJobPublisher
{
    Task PublishAsync(UserSyncJob job, CancellationToken cancellationToken = default);

    Task PublishDeadLetterAsync(UserSyncJob job, CancellationToken cancellationToken = default);
}
