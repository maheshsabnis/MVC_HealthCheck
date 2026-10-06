using Microsoft.EntityFrameworkCore;
using MVC_HealthCheck.Data;
using MVC_HealthCheck.Messaging;
using MVC_HealthCheck.Models;
using MVC_HealthCheck.ViewModels;

namespace MVC_HealthCheck.Services;

/// <summary>
/// Order capture: writes the order to SQL Server, records it on the Redis "received" list and
/// hands it to RabbitMQ for the background worker to decide on.
/// </summary>
public sealed class OrderService
{
    private readonly OrdersDbContext _db;
    private readonly IOrderPublisher _publisher;
    private readonly IOrderCache _cache;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        OrdersDbContext db,
        IOrderPublisher publisher,
        IOrderCache cache,
        ILogger<OrderService> logger)
    {
        _db = db;
        _publisher = publisher;
        _cache = cache;
        _logger = logger;
    }

    public Task<List<Customer>> GetCustomersAsync(CancellationToken ct = default) =>
        _db.Customers.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);

    public Task<List<Product>> GetProductsAsync(CancellationToken ct = default) =>
        _db.Products.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct);

    /// <summary>
    /// Persists the order and publishes it. Pricing is recomputed from the catalogue rather than
    /// taken from the posted form, so a tampered price cannot change the accept/reject outcome.
    /// </summary>
    public async Task<Order> CreateOrderAsync(CreateOrderViewModel model, CancellationToken ct = default)
    {
        var lines = model.Items
            .Where(i => i.ProductId > 0 && i.Quantity > 0)
            .GroupBy(i => i.ProductId)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToList();

        if (lines.Count == 0)
        {
            throw new InvalidOperationException("An order needs at least one product line.");
        }

        var productIds = lines.Select(l => l.ProductId).ToList();
        var products = await _db.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        var missing = productIds.Where(id => !products.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"Unknown product id(s): {string.Join(", ", missing)}.");
        }

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == model.CustomerId, ct)
            ?? throw new InvalidOperationException($"Unknown customer id {model.CustomerId}.");

        var order = new Order
        {
            OrderNumber = GenerateOrderNumber(),
            CustomerId = customer.Id,
            OrderDate = DateTime.UtcNow,
            AdvanceAmount = model.AdvanceAmount,
            Status = OrderStatus.Received,
            StatusMessage = "Captured, awaiting processing."
        };

        foreach (var line in lines)
        {
            var product = products[line.ProductId];
            order.OrderDetails.Add(new OrderDetail
            {
                ProductId = product.Id,
                Quantity = line.Quantity,
                UnitPrice = product.UnitPrice,
                LineTotal = product.UnitPrice * line.Quantity
            });
        }

        order.TotalAmount = order.OrderDetails.Sum(d => d.LineTotal);

        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);

        var cacheEntry = new OrderCacheEntry
        {
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            CustomerName = customer.Name,
            TotalAmount = order.TotalAmount,
            AdvanceAmount = order.AdvanceAmount,
            AdvancePercentage = order.AdvancePercentage,
            Status = "Received",
            TimestampUtc = order.OrderDate
        };

        await _cache.RecordReceivedAsync(cacheEntry, ct);

        try
        {
            await _publisher.PublishAsync(new OrderMessage
            {
                OrderId = order.Id,
                OrderNumber = order.OrderNumber,
                CustomerId = customer.Id,
                CustomerName = customer.Name,
                TotalAmount = order.TotalAmount,
                AdvanceAmount = order.AdvanceAmount,
                ItemCount = order.OrderDetails.Count,
                OrderPlacedOnUtc = order.OrderDate
            }, ct);

            order.Status = OrderStatus.Queued;
            order.StatusMessage = "Queued for processing.";
        }
        catch (Exception ex)
        {
            // The order is already durable in SQL Server. Surface the broker problem on the
            // status screen instead of losing the capture.
            _logger.LogError(ex, "Order {OrderNumber} was saved but could not be queued.", order.OrderNumber);
            order.Status = OrderStatus.Failed;
            order.StatusMessage = $"Saved, but publishing to RabbitMQ failed: {ex.Message}";
        }

        await _db.SaveChangesAsync(ct);
        return order;
    }

    public Task<List<Order>> GetOrdersAsync(OrderStatus? status = null, CancellationToken ct = default)
    {
        var query = _db.Orders
            .AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.OrderDetails)
                .ThenInclude(d => d.Product)
            .AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(o => o.Status == status.Value);
        }

        return query.OrderByDescending(o => o.Id).ToListAsync(ct);
    }

    public Task<Order?> GetOrderAsync(int id, CancellationToken ct = default) =>
        _db.Orders
            .AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.OrderDetails)
                .ThenInclude(d => d.Product)
            .FirstOrDefaultAsync(o => o.Id == id, ct);

    /// <summary>Short, sortable, collision-resistant enough for a single-node demo.</summary>
    private static string GenerateOrderNumber() =>
        $"ORD-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
}
