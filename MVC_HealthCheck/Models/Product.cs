using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MVC_HealthCheck.Models;

/// <summary>SQL Server entity. Catalogue item that order lines point at.</summary>
public class Product
{
    public int Id { get; set; }

    [Required, StringLength(150)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(50)]
    public string Sku { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    [Range(0.01, 1_000_000)]
    public decimal UnitPrice { get; set; }

    [Range(0, int.MaxValue)]
    public int StockQuantity { get; set; }

    public ICollection<OrderDetail> OrderDetails { get; set; } = new List<OrderDetail>();
}
