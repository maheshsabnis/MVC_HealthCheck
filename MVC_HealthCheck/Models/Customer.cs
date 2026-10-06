using System.ComponentModel.DataAnnotations;

namespace MVC_HealthCheck.Models;

/// <summary>SQL Server entity. Placed an order is always tied to exactly one customer.</summary>
public class Customer
{
    public int Id { get; set; }

    [Required, StringLength(150)]
    public string Name { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(200)]
    public string Email { get; set; } = string.Empty;

    [StringLength(20)]
    public string? Phone { get; set; }

    [StringLength(100)]
    public string? City { get; set; }

    public ICollection<Order> Orders { get; set; } = new List<Order>();
}
