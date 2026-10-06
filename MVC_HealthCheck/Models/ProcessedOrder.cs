using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MVC_HealthCheck.Models;

/// <summary>
/// PostgreSQL entity. The outcome record the background worker writes after applying the
/// advance rule. Deliberately denormalised: it is a read model and never joins back to SQL Server.
/// </summary>
[Table("processed_orders")]
public class ProcessedOrder
{
    [Column("id")]
    public int Id { get; set; }

    /// <summary>Primary key of the originating row in the SQL Server Orders table.</summary>
    [Column("order_id")]
    public int OrderId { get; set; }

    [Column("order_number"), StringLength(30)]
    public string OrderNumber { get; set; } = string.Empty;

    [Column("customer_id")]
    public int CustomerId { get; set; }

    [Column("customer_name"), StringLength(150)]
    public string CustomerName { get; set; } = string.Empty;

    [Column("total_amount", TypeName = "numeric(18,2)")]
    public decimal TotalAmount { get; set; }

    [Column("advance_amount", TypeName = "numeric(18,2)")]
    public decimal AdvanceAmount { get; set; }

    [Column("advance_percentage", TypeName = "numeric(9,2)")]
    public decimal AdvancePercentage { get; set; }

    /// <summary>"Accepted" or "Rejected". Stored as text so the read model stays decoupled from the enum.</summary>
    [Column("decision"), StringLength(20)]
    public string Decision { get; set; } = string.Empty;

    [Column("reason"), StringLength(400)]
    public string Reason { get; set; } = string.Empty;

    [Column("item_count")]
    public int ItemCount { get; set; }

    [Column("order_placed_on_utc")]
    public DateTime OrderPlacedOnUtc { get; set; }

    [Column("processed_on_utc")]
    public DateTime ProcessedOnUtc { get; set; }

    [Column("processing_duration_ms")]
    public long ProcessingDurationMs { get; set; }

    [NotMapped]
    public bool IsAccepted => string.Equals(Decision, "Accepted", StringComparison.OrdinalIgnoreCase);
}
