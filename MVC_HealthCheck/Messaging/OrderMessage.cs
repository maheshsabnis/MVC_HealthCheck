namespace MVC_HealthCheck.Messaging;

/// <summary>
/// The RabbitMQ payload. Carries everything the worker needs so it does not have to read
/// back from SQL Server before making the accept/reject decision.
/// </summary>
public sealed record OrderMessage
{
    public required int OrderId { get; init; }
    public required string OrderNumber { get; init; }
    public required int CustomerId { get; init; }
    public required string CustomerName { get; init; }
    public required decimal TotalAmount { get; init; }
    public required decimal AdvanceAmount { get; init; }
    public required int ItemCount { get; init; }
    public required DateTime OrderPlacedOnUtc { get; init; }
}
