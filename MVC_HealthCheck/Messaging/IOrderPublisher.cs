namespace MVC_HealthCheck.Messaging;

/// <summary>Publishes a captured order onto the RabbitMQ work queue.</summary>
public interface IOrderPublisher
{
    Task PublishAsync(OrderMessage message, CancellationToken ct = default);
}
