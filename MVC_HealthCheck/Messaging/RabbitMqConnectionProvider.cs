using Microsoft.Extensions.Options;
using MVC_HealthCheck.Options;
using RabbitMQ.Client;

namespace MVC_HealthCheck.Messaging;

/// <summary>
/// Owns the single long-lived RabbitMQ connection shared by the publisher, the consumer and
/// the rabbitmq health check.
///
/// The connection is created lazily and re-created when it drops, so the broker can be
/// restarted underneath a running app and the health check will recover on its own instead of
/// pinning itself to a dead connection.
/// </summary>
public sealed class RabbitMqConnectionProvider : IAsyncDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqConnectionProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IConnection? _connection;
    private bool _disposed;

    public RabbitMqConnectionProvider(IOptions<RabbitMqOptions> options, ILogger<RabbitMqConnectionProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public string QueueName => _options.QueueName;

    public async Task<IConnection> GetConnectionAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var existing = _connection;
        if (existing is { IsOpen: true })
        {
            return existing;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }

            if (_connection is not null)
            {
                _logger.LogWarning("RabbitMQ connection was closed. Reconnecting.");
                await SafeDisposeAsync(_connection);
                _connection = null;
            }

            var factory = new ConnectionFactory
            {
                HostName = _options.HostName,
                Port = _options.Port,
                UserName = _options.UserName,
                Password = _options.Password,
                VirtualHost = _options.VirtualHost,
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true
            };

            _connection = await factory.CreateConnectionAsync("mvc-healthcheck-order-processing", ct);
            _logger.LogInformation("Connected to RabbitMQ at {Host}:{Port}.", _options.HostName, _options.Port);
            return _connection;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Declares the durable work queue. Idempotent, so both publisher and consumer call it.</summary>
    public async Task DeclareQueueAsync(IChannel channel, CancellationToken ct = default)
    {
        await channel.QueueDeclareAsync(
            queue: _options.QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_connection is not null)
        {
            await SafeDisposeAsync(_connection);
            _connection = null;
        }

        _gate.Dispose();
    }

    private async Task SafeDisposeAsync(IConnection connection)
    {
        try
        {
            await connection.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Ignoring error while disposing a RabbitMQ connection.");
        }
    }
}
