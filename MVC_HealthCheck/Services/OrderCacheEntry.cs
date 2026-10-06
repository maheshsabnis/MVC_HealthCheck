namespace MVC_HealthCheck.Services;

/// <summary>A row in one of the Redis activity lists shown on the dashboard.</summary>
public sealed record OrderCacheEntry
{
    public required int OrderId { get; init; }
    public required string OrderNumber { get; init; }
    public required string CustomerName { get; init; }
    public required decimal TotalAmount { get; init; }
    public required decimal AdvanceAmount { get; init; }
    public required decimal AdvancePercentage { get; init; }

    /// <summary>"Received" for the inbound list, "Accepted"/"Rejected" for the processed list.</summary>
    public required string Status { get; init; }

    public string? Reason { get; init; }
    public required DateTime TimestampUtc { get; init; }
}

/// <summary>Counters rendered on the dashboard tiles.</summary>
public sealed record OrderCacheCounters
{
    public long Received { get; init; }
    public long Processed { get; init; }
    public long Accepted { get; init; }
    public long Rejected { get; init; }
}
