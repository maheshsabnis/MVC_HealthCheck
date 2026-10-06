namespace MVC_HealthCheck.Services;

/// <summary>
/// Redis-backed activity cache. Every method swallows connection errors and degrades to an
/// empty result, so a Redis outage shows up on /health without taking the UI down.
/// </summary>
public interface IOrderCache
{
    Task RecordReceivedAsync(OrderCacheEntry entry, CancellationToken ct = default);
    Task RecordProcessedAsync(OrderCacheEntry entry, CancellationToken ct = default);

    Task<IReadOnlyList<OrderCacheEntry>> GetReceivedAsync(int count = 25, CancellationToken ct = default);
    Task<IReadOnlyList<OrderCacheEntry>> GetProcessedAsync(int count = 25, CancellationToken ct = default);

    Task<OrderCacheCounters> GetCountersAsync(CancellationToken ct = default);

    /// <summary>True when the multiplexer currently has a live connection to Redis.</summary>
    bool IsAvailable { get; }
}
