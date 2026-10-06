using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MVC_HealthCheck.Data;
using MVC_HealthCheck.HealthChecks;
using MVC_HealthCheck.Messaging;
using MVC_HealthCheck.Options;
using MVC_HealthCheck.Services;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------- configuration --

builder.Services.Configure<RabbitMqOptions>(
    builder.Configuration.GetSection(RabbitMqOptions.SectionName));
builder.Services.Configure<OrderProcessingOptions>(
    builder.Configuration.GetSection(OrderProcessingOptions.SectionName));

var sqlServerConnection = builder.Configuration.GetConnectionString("SqlServer")
    ?? throw new InvalidOperationException("ConnectionStrings:SqlServer is not configured.");
var postgresConnection = builder.Configuration.GetConnectionString("PostgreSql")
    ?? throw new InvalidOperationException("ConnectionStrings:PostgreSql is not configured.");
var redisConnection = builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException("ConnectionStrings:Redis is not configured.");

// ---------------------------------------------------------------------------------- storage --

// SQL Server holds Customers, Products, Orders and OrderDetails.
builder.Services.AddDbContext<OrdersDbContext>(options =>
    options.UseSqlServer(sqlServerConnection, sql => sql.EnableRetryOnFailure()));

// PostgreSQL holds the processed_orders outcome table.
builder.Services.AddDbContext<ProcessedOrdersDbContext>(options =>
    options.UseNpgsql(postgresConnection, npg => npg.EnableRetryOnFailure()));

// A single multiplexer for the whole app. AbortOnConnectFail=false means a Redis outage at
// startup does not stop the app from booting; it shows up on /health instead.
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var config = ConfigurationOptions.Parse(redisConnection);
    config.AbortOnConnectFail = false;
    config.ConnectRetry = 3;
    config.ClientName = "mvc-healthcheck-order-processing";
    return ConnectionMultiplexer.Connect(config);
});

builder.Services.AddSingleton<IOrderCache, RedisOrderCache>();

// ---------------------------------------------------------------------------------- messaging --

builder.Services.AddSingleton<RabbitMqConnectionProvider>();
builder.Services.AddSingleton<IOrderPublisher, RabbitMqOrderPublisher>();
builder.Services.AddHostedService<OrderProcessingWorker>();

// ---------------------------------------------------------------------------------- app services --

builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<ProcessedOrderService>();

// ------------------------------------------------------------------------------- health checks --
// AspNetCore.Diagnostics.HealthChecks (Xabaril) probes for every external dependency.

builder.Services.AddHealthChecks()
    .AddSqlServer(
        connectionString: sqlServerConnection,
        healthQuery: "SELECT 1;",
        name: "sql-server",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["db", "sql", "sqlserver", "ready"])

    .AddNpgSql(
        connectionString: postgresConnection,
        healthQuery: "SELECT 1;",
        name: "postgresql",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["db", "sql", "postgres", "ready"])

    .AddRedis(
        connectionMultiplexerFactory: sp => sp.GetRequiredService<IConnectionMultiplexer>(),
        name: "redis",
        failureStatus: HealthStatus.Degraded,
        tags: ["cache", "redis", "ready"])

    // The async overload: the factory runs on every probe, so the provider gets a chance to
    // re-establish a dropped connection instead of reporting a stale failure forever.
    .AddRabbitMQ(
        factory: sp => sp.GetRequiredService<RabbitMqConnectionProvider>().GetConnectionAsync(),
        name: "rabbitmq",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["broker", "rabbitmq", "ready"]);

// The polling UI that ships with the same library.
builder.Services
    .AddHealthChecksUI(settings =>
    {
        settings.SetEvaluationTimeInSeconds(15);
        settings.MaximumHistoryEntriesPerEndpoint(60);
        settings.SetApiMaxActiveRequests(1);
        // Must point at the UI-formatted payload, not the custom /health JSON.
        settings.AddHealthCheckEndpoint("Order Processing", "/health/ui-data");
    })
    .AddInMemoryStorage();

// ---------------------------------------------------------------------------------------- mvc --

builder.Services.AddControllersWithViews();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapStaticAssets();

// ------------------------------------------------------------------------- health endpoints --

// Everything, with the detailed JSON body.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = HealthCheckResponseWriter.WriteAsync
});

// Readiness: all external dependencies must answer before traffic is routed here.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = HealthCheckResponseWriter.WriteAsync
});

// Liveness: is the process up at all? No dependency is probed.
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});

// The shape the HealthChecks UI polls for.
app.MapHealthChecks("/health/ui-data", new HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.MapHealthChecksUI(options => options.UIPath = "/health-ui");

// ---------------------------------------------------------------------------------- routing --

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

// Create both schemas and seed the catalogue. Failures are logged, not thrown, so a database
// outage surfaces on /health rather than preventing startup.
await DatabaseInitializer.InitializeAsync(app.Services);

app.Run();
