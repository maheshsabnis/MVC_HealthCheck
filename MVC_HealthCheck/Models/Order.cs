using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MVC_HealthCheck.Models;

/// <summary>SQL Server entity. The order as captured by the web tier, before processing.</summary>
public class Order
{
    public int Id { get; set; }

    [Required, StringLength(30)]
    public string OrderNumber { get; set; } = string.Empty;

    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public DateTime OrderDate { get; set; } = DateTime.UtcNow;

    /// <summary>Sum of all order line totals. Computed server side, never trusted from the form.</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalAmount { get; set; }

    /// <summary>Money paid up front. The accept/reject rule compares this against <see cref="TotalAmount"/>.</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal AdvanceAmount { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Received;

    [StringLength(500)]
    public string? StatusMessage { get; set; }

    public DateTime? ProcessedOn { get; set; }

    public ICollection<OrderDetail> OrderDetails { get; set; } = new List<OrderDetail>();

    /// <summary>Advance as a percentage of the total. Zero-total orders are treated as 0%.</summary>
    [NotMapped]
    public decimal AdvancePercentage =>
        TotalAmount <= 0 ? 0 : Math.Round(AdvanceAmount / TotalAmount * 100m, 2);
}
