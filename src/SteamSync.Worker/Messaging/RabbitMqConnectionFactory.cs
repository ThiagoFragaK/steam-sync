using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using SteamSync.Worker.Configuration;

namespace SteamSync.Worker.Messaging;

public interface IRabbitMqConnectionFactory
{
    Task<IConnection> CreateConnectionAsync(CancellationToken cancellationToken = default);
}

public sealed class RabbitMqConnectionFactory : IRabbitMqConnectionFactory
{
    private readonly RabbitMqOptions _options;

    public RabbitMqConnectionFactory(IOptions<RabbitMqOptions> options)
    {
        _options = options.Value;
    }

    public Task<IConnection> CreateConnectionAsync(CancellationToken cancellationToken = default)
    {
        var factory = CreateFactory();
        return factory.CreateConnectionAsync(cancellationToken);
    }

    private ConnectionFactory CreateFactory()
    {
        if (!string.IsNullOrWhiteSpace(_options.Url))
        {
            return new ConnectionFactory { Uri = new Uri(_options.Url) };
        }

        return new ConnectionFactory
        {
            HostName = _options.Host,
            Port = _options.Port,
            UserName = _options.Username,
            Password = _options.Password,
            VirtualHost = string.IsNullOrWhiteSpace(_options.VirtualHost) ? "/" : _options.VirtualHost
        };
    }
}
