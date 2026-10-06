namespace MVC_HealthCheck.Options;

/// <summary>Bound from the "OrderProcessing" section of appsettings.json.</summary>
public sealed class OrderProcessingOptions
{
    public const string SectionName = "OrderProcessing";

    /// <summary>
    /// Minimum advance, as a percentage of the order total, for an order to be accepted.
    /// The business rule in the brief is 50%.
    /// </summary>
    public decimal MinimumAdvancePercentage { get; set; } = 50m;

    /// <summary>Artificial delay so the Queued -> Processing -> Accepted transition is visible in the UI.</summary>
    public int SimulatedWorkMilliseconds { get; set; } = 750;

    /// <summary>How many recent orders each Redis activity list keeps.</summary>
    public int CacheListLength { get; set; } = 50;
}
