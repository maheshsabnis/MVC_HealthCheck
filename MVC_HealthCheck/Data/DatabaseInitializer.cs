using Microsoft.EntityFrameworkCore;
using MVC_HealthCheck.Models;

namespace MVC_HealthCheck.Data;

/// <summary>
/// Creates both schemas on startup and seeds the SQL Server catalogue.
/// Failures are logged rather than thrown: a database being down should surface through the
/// health endpoint, not prevent the app from booting.
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseInitializer");

        await InitializeSqlServerAsync(scope.ServiceProvider, logger, cancellationToken);
        await InitializePostgreSqlAsync(scope.ServiceProvider, logger, cancellationToken);
    }

    private static async Task InitializeSqlServerAsync(IServiceProvider sp, ILogger logger, CancellationToken ct)
    {
        try
        {
            var db = sp.GetRequiredService<OrdersDbContext>();
            await db.Database.EnsureCreatedAsync(ct);
            await SeedCatalogueAsync(db, ct);
            logger.LogInformation("SQL Server schema ready (Customers, Products, Orders, OrderDetails).");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SQL Server initialisation failed. The sql-server health check will report Unhealthy.");
        }
    }

    private static async Task InitializePostgreSqlAsync(IServiceProvider sp, ILogger logger, CancellationToken ct)
    {
        try
        {
            var db = sp.GetRequiredService<ProcessedOrdersDbContext>();
            await db.Database.EnsureCreatedAsync(ct);
            logger.LogInformation("PostgreSQL schema ready (processed_orders).");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PostgreSQL initialisation failed. The postgresql health check will report Unhealthy.");
        }
    }

    private static async Task SeedCatalogueAsync(OrdersDbContext db, CancellationToken ct)
    {
        if (!await db.Customers.AnyAsync(ct))
        {
            db.Customers.AddRange(
                new Customer { Name = "Mahesh Sabnis", Email = "mahesh@example.com", Phone = "9890000001", City = "Pune" },
                new Customer { Name = "Leena Kulkarni", Email = "leena@example.com", Phone = "9890000002", City = "Mumbai" },
                new Customer { Name = "Arjun Deshpande", Email = "arjun@example.com", Phone = "9890000003", City = "Bengaluru" },
                new Customer { Name = "Fatima Shaikh", Email = "fatima@example.com", Phone = "9890000004", City = "Hyderabad" },
                new Customer { Name = "Rohit Nair", Email = "rohit@example.com", Phone = "9890000005", City = "Kochi" });
        }

        if (!await db.Products.AnyAsync(ct))
        {
            db.Products.AddRange(
                new Product { Name = "Mechanical Keyboard",   Sku = "KBD-MECH-87",  UnitPrice = 7_499.00m,  StockQuantity = 120 },
                new Product { Name = "27\" 4K Monitor",       Sku = "MON-4K-27",    UnitPrice = 32_900.00m, StockQuantity = 45  },
                new Product { Name = "Wireless Mouse",        Sku = "MSE-WL-02",    UnitPrice = 2_250.00m,  StockQuantity = 300 },
                new Product { Name = "USB-C Docking Station", Sku = "DCK-USBC-11",  UnitPrice = 14_750.00m, StockQuantity = 60  },
                new Product { Name = "Noise Cancelling Headset", Sku = "HDS-NC-44", UnitPrice = 18_999.00m, StockQuantity = 80  },
                new Product { Name = "Laptop Stand (Aluminium)", Sku = "STD-ALU-09", UnitPrice = 3_199.00m, StockQuantity = 210 },
                new Product { Name = "1080p Webcam",          Sku = "CAM-HD-10",    UnitPrice = 5_650.00m,  StockQuantity = 150 },
                new Product { Name = "2TB NVMe SSD",          Sku = "SSD-NVME-2T",  UnitPrice = 16_400.00m, StockQuantity = 95  });
        }

        await db.SaveChangesAsync(ct);
    }
}
