using Microsoft.EntityFrameworkCore;
using MVC_HealthCheck.Models;

namespace MVC_HealthCheck.Data;

/// <summary>
/// SQL Server write model: Customers, Products, Orders, OrderDetails.
/// This is the system of record for orders as they are captured.
/// </summary>
public class OrdersDbContext : DbContext
{
    public OrdersDbContext(DbContextOptions<OrdersDbContext> options) : base(options) { }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderDetail> OrderDetails => Set<OrderDetail>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(e =>
        {
            e.ToTable("Customers");
            e.HasIndex(c => c.Email).IsUnique();
        });

        modelBuilder.Entity<Product>(e =>
        {
            e.ToTable("Products");
            e.HasIndex(p => p.Sku).IsUnique();
        });

        modelBuilder.Entity<Order>(e =>
        {
            e.ToTable("Orders");
            e.HasIndex(o => o.OrderNumber).IsUnique();
            e.HasIndex(o => o.Status);

            // Status is persisted as text so the table stays readable from SSMS.
            e.Property(o => o.Status)
                .HasConversion<string>()
                .HasMaxLength(20);

            e.HasOne(o => o.Customer)
                .WithMany(c => c.Orders)
                .HasForeignKey(o => o.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OrderDetail>(e =>
        {
            e.ToTable("OrderDetails");

            e.HasOne(d => d.Order)
                .WithMany(o => o.OrderDetails)
                .HasForeignKey(d => d.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(d => d.Product)
                .WithMany(p => p.OrderDetails)
                .HasForeignKey(d => d.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        base.OnModelCreating(modelBuilder);
    }
}
