namespace MVC_HealthCheck.Models;

/// <summary>
/// Lifecycle of an order as it moves SQL Server -> RabbitMQ -> background worker -> PostgreSQL.
/// </summary>
public enum OrderStatus
{
    /// <summary>Persisted to SQL Server but not yet handed to the broker.</summary>
    Received = 0,

    /// <summary>Published to RabbitMQ, waiting for the background worker.</summary>
    Queued = 1,

    /// <summary>The worker picked the message up and is applying the advance rule.</summary>
    Processing = 2,

    /// <summary>Advance was at least 50% of the total. Written to PostgreSQL.</summary>
    Accepted = 3,

    /// <summary>Advance was below 50% of the total. Written to PostgreSQL.</summary>
    Rejected = 4,

    /// <summary>The worker could not process the message (infrastructure error).</summary>
    Failed = 5
}
