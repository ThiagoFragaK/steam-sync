using RabbitMQ.Client;

namespace SteamSync.Worker.Infrastructure.Messaging.Interfaces;
public interface IRabbitMqConnectionFactory
{
    Task<IConnection> CreateConnectionAsync(CancellationToken cancellationToken = default);
}
