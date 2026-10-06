using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using MVC_HealthCheck.Models;
using MVC_HealthCheck.Services;

namespace MVC_HealthCheck.ViewModels;

/// <summary>One product line on the create-order form.</summary>
public class OrderLineInput
{
    [Display(Name = "Product")]
    public int ProductId { get; set; }

    [Display(Name = "Quantity")]
    [Range(1, 10_000, ErrorMessage = "Quantity must be between 1 and 10,000.")]
    public int Quantity { get; set; } = 1;
}

/// <summary>Backs the Create Order screen.</summary>
public class CreateOrderViewModel : IValidatableObject
{
    [Display(Name = "Customer")]
    [Range(1, int.MaxValue, ErrorMessage = "Please choose a customer.")]
    public int CustomerId { get; set; }

    [Display(Name = "Advance Paid")]
    [Range(0, 100_000_000, ErrorMessage = "Advance must be zero or more.")]
    [DataType(DataType.Currency)]
    public decimal AdvanceAmount { get; set; }

    public List<OrderLineInput> Items { get; set; } = [new OrderLineInput()];

    // Populated by the controller for the dropdowns.
    public IEnumerable<SelectListItem> CustomerOptions { get; set; } = [];
    public IEnumerable<Product> Products { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Items is null || Items.All(i => i.ProductId <= 0))
        {
            yield return new ValidationResult(
                "Add at least one product line.", [nameof(Items)]);
        }
    }
}

/// <summary>Backs the Order Status screen (SQL Server side of the pipeline).</summary>
public class OrderStatusViewModel
{
    public IReadOnlyList<Order> Orders { get; init; } = [];
    public OrderStatus? Filter { get; init; }
    public OrderCacheCounters Counters { get; init; } = new();
    public bool CacheAvailable { get; init; }
}

/// <summary>Backs both the Processed Orders screen and the Accepted/Rejected screen (PostgreSQL side).</summary>
public class ProcessedOrdersViewModel
{
    public IReadOnlyList<ProcessedOrder> ProcessedOrders { get; init; } = [];

    /// <summary>null = all, "Accepted", or "Rejected".</summary>
    public string? Decision { get; init; }

    public int AcceptedCount { get; init; }
    public int RejectedCount { get; init; }
    public decimal AcceptedValue { get; init; }
    public decimal RejectedValue { get; init; }

    /// <summary>Set when PostgreSQL could not be reached, so the view can explain the empty table.</summary>
    public string? DataError { get; init; }
}

/// <summary>Backs the dashboard, which is driven mostly by the Redis activity lists.</summary>
public class DashboardViewModel
{
    public OrderCacheCounters Counters { get; init; } = new();
    public IReadOnlyList<OrderCacheEntry> RecentReceived { get; init; } = [];
    public IReadOnlyList<OrderCacheEntry> RecentProcessed { get; init; } = [];
    public bool CacheAvailable { get; init; }
    public int PendingInPipeline { get; init; }
}
