using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

namespace MVC_HealthCheck.Messaging;

/// <inheritdoc cref="IOrderPublisher"/>
public sealed class RabbitMqOrderPublisher : IOrderPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RabbitMqConnectionProvider _connections;
    private readonly ILogger<RabbitMqOrderPublisher> _logger;

    public RabbitMqOrderPublisher(RabbitMqConnectionProvider connections, ILogger<RabbitMqOrderPublisher> logger)
    {
        _connections = connections;
        _logger = logger;
    }

    public async Task PublishAsync(OrderMessage message, CancellationToken ct = default)
    {
        var connection = await _connections.GetConnectionAsync(ct);

        // A channel is cheap and is not thread safe, so each publish gets its own.
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
        await _connections.DeclareQueueAsync(channel, ct);

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, JsonOptions));

        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            MessageId = message.OrderNumber,
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        };

        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: _connections.QueueName,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: ct);

        _logger.LogInformation("Published order {OrderNumber} to queue {Queue}.",
            message.OrderNumber, _connections.QueueName);
    }
}
