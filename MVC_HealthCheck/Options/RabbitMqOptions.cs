namespace MVC_HealthCheck.Options;

/// <summary>Bound from the "RabbitMq" section of appsettings.json.</summary>
public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";

    /// <summary>Durable queue the web tier publishes to and the background worker consumes from.</summary>
    public string QueueName { get; set; } = "order-processing-queue";
}
