using Microsoft.EntityFrameworkCore;
using MVC_HealthCheck.Data;
using MVC_HealthCheck.Models;
using MVC_HealthCheck.ViewModels;

namespace MVC_HealthCheck.Services;

/// <summary>Reads the PostgreSQL outcome table for the Processed and Accepted/Rejected screens.</summary>
public sealed class ProcessedOrderService
{
    private readonly ProcessedOrdersDbContext _db;
    private readonly ILogger<ProcessedOrderService> _logger;

    public ProcessedOrderService(ProcessedOrdersDbContext db, ILogger<ProcessedOrderService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <param name="decision">null for everything, otherwise "Accepted" or "Rejected".</param>
    public async Task<ProcessedOrdersViewModel> GetAsync(string? decision, CancellationToken ct = default)
    {
        try
        {
            var all = await _db.ProcessedOrders
                .AsNoTracking()
                .OrderByDescending(p => p.ProcessedOnUtc)
                .ToListAsync(ct);

            var filtered = string.IsNullOrWhiteSpace(decision)
                ? all
                : all.Where(p => string.Equals(p.Decision, decision, StringComparison.OrdinalIgnoreCase)).ToList();

            return new ProcessedOrdersViewModel
            {
                ProcessedOrders = filtered,
                Decision = decision,
                AcceptedCount = all.Count(p => p.IsAccepted),
                RejectedCount = all.Count(p => !p.IsAccepted),
                AcceptedValue = all.Where(p => p.IsAccepted).Sum(p => p.TotalAmount),
                RejectedValue = all.Where(p => !p.IsAccepted).Sum(p => p.TotalAmount)
            };
        }
        catch (Exception ex)
        {
            // PostgreSQL being down is already reported by /health; the screen should say so
            // rather than return a 500.
            _logger.LogError(ex, "Could not read processed orders from PostgreSQL.");
            return new ProcessedOrdersViewModel
            {
                Decision = decision,
                DataError = $"PostgreSQL is not reachable: {ex.Message}"
            };
        }
    }

    public async Task<ProcessedOrder?> GetByOrderIdAsync(int orderId, CancellationToken ct = default)
    {
        try
        {
            return await _db.ProcessedOrders.AsNoTracking().FirstOrDefaultAsync(p => p.OrderId == orderId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not read processed order for order {OrderId}.", orderId);
            return null;
        }
    }
}
