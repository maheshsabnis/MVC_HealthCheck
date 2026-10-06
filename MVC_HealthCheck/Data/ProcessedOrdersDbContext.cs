using Microsoft.EntityFrameworkCore;
using MVC_HealthCheck.Models;

namespace MVC_HealthCheck.Data;

/// <summary>
/// PostgreSQL read model: the single processed_orders table the background worker writes to
/// once the advance rule has been applied.
/// </summary>
public class ProcessedOrdersDbContext : DbContext
{
    public ProcessedOrdersDbContext(DbContextOptions<ProcessedOrdersDbContext> options) : base(options) { }

    public DbSet<ProcessedOrder> ProcessedOrders => Set<ProcessedOrder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProcessedOrder>(e =>
        {
            e.ToTable("processed_orders");

            // The worker is idempotent: a redelivered message must not create a second row.
            e.HasIndex(p => p.OrderId).IsUnique();
            e.HasIndex(p => p.Decision);
            e.HasIndex(p => p.ProcessedOnUtc);

            e.Property(p => p.OrderPlacedOnUtc).HasColumnType("timestamp with time zone");
            e.Property(p => p.ProcessedOnUtc).HasColumnType("timestamp with time zone");
        });

        base.OnModelCreating(modelBuilder);
    }
}
