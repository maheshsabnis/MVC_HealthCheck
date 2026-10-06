using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MVC_HealthCheck.Models;

/// <summary>SQL Server entity. One product line on an order.</summary>
public class OrderDetail
{
    public int Id { get; set; }

    public int OrderId { get; set; }
    public Order? Order { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    [Range(1, 10_000)]
    public int Quantity { get; set; }

    /// <summary>Price snapshot taken when the order was placed, so later catalogue edits do not rewrite history.</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitPrice { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal LineTotal { get; set; }
}
