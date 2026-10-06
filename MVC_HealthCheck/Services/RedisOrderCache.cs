using System.Text.Json;
using Microsoft.Extensions.Options;
using MVC_HealthCheck.Options;
using StackExchange.Redis;

namespace MVC_HealthCheck.Services;

/// <inheritdoc cref="IOrderCache"/>
public sealed class RedisOrderCache : IOrderCache
{
    // Keys are grouped under an "orders:" prefix so they are easy to scan in redis-cli.
    private const string ReceivedListKey = "orders:list:received";
    private const string ProcessedListKey = "orders:list:processed";
    private const string ReceivedCounterKey = "orders:count:received";
    private const string ProcessedCounterKey = "orders:count:processed";
    private const string AcceptedCounterKey = "orders:count:accepted";
    private const string RejectedCounterKey = "orders:count:rejected";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IConnectionMultiplexer _multiplexer;
    private readonly ILogger<RedisOrderCache> _logger;
    private readonly int _listLength;

    public RedisOrderCache(
        IConnectionMultiplexer multiplexer,
        IOptions<OrderProcessingOptions> options,
        ILogger<RedisOrderCache> logger)
    {
        _multiplexer = multiplexer;
        _logger = logger;
        _listLength = Math.Max(1, options.Value.CacheListLength);
    }

    public bool IsAvailable => _multiplexer.IsConnected;

    public Task RecordReceivedAsync(OrderCacheEntry entry, CancellationToken ct = default) =>
        PushAsync(ReceivedListKey, entry, [ReceivedCounterKey]);

    public Task RecordProcessedAsync(OrderCacheEntry entry, CancellationToken ct = default)
    {
        var counters = entry.Status.Equals("Accepted", StringComparison.OrdinalIgnoreCase)
            ? new[] { ProcessedCounterKey, AcceptedCounterKey }
            : new[] { ProcessedCounterKey, RejectedCounterKey };

        return PushAsync(ProcessedListKey, entry, counters);
    }

    public Task<IReadOnlyList<OrderCacheEntry>> GetReceivedAsync(int count = 25, CancellationToken ct = default) =>
        RangeAsync(ReceivedListKey, count);

    public Task<IReadOnlyList<OrderCacheEntry>> GetProcessedAsync(int count = 25, CancellationToken ct = default) =>
        RangeAsync(ProcessedListKey, count);

    public async Task<OrderCacheCounters> GetCountersAsync(CancellationToken ct = default)
    {
        try
        {
            var db = _multiplexer.GetDatabase();
            var values = await db.StringGetAsync(
            [
                ReceivedCounterKey, ProcessedCounterKey, AcceptedCounterKey, RejectedCounterKey
            ]);

            return new OrderCacheCounters
            {
                Received = ToLong(values[0]),
                Processed = ToLong(values[1]),
                Accepted = ToLong(values[2]),
                Rejected = ToLong(values[3])
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read order counters from Redis.");
            return new OrderCacheCounters();
        }
    }

    private async Task PushAsync(string listKey, OrderCacheEntry entry, string[] counterKeys)
    {
        try
        {
            var db = _multiplexer.GetDatabase();
            var payload = JsonSerializer.Serialize(entry, JsonOptions);

            // Newest first, then trim so the list cannot grow without bound.
            var batch = db.CreateBatch();
            var tasks = new List<Task> { batch.ListLeftPushAsync(listKey, payload) };
            tasks.Add(batch.ListTrimAsync(listKey, 0, _listLength - 1));
            tasks.AddRange(counterKeys.Select(k => (Task)batch.StringIncrementAsync(k)));
            batch.Execute();

            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not write order {OrderNumber} to the Redis list {ListKey}.",
                entry.OrderNumber, listKey);
        }
    }

    private async Task<IReadOnlyList<OrderCacheEntry>> RangeAsync(string listKey, int count)
    {
        try
        {
            var db = _multiplexer.GetDatabase();
            var values = await db.ListRangeAsync(listKey, 0, Math.Max(1, count) - 1);

            return values
                .Select(v => v.HasValue ? SafeDeserialize(v!) : null)
                .Where(e => e is not null)
                .Select(e => e!)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the Redis list {ListKey}.", listKey);
            return [];
        }
    }

    private OrderCacheEntry? SafeDeserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<OrderCacheEntry>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Skipping malformed cache entry.");
            return null;
        }
    }

    private static long ToLong(RedisValue value) => value.HasValue && value.TryParse(out long l) ? l : 0;
}
