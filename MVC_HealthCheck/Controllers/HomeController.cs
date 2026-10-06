using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MVC_HealthCheck.Models;
using MVC_HealthCheck.Services;
using MVC_HealthCheck.ViewModels;

namespace MVC_HealthCheck.Controllers;

public class HomeController : Controller
{
    private readonly IOrderCache _cache;
    private readonly OrderService _orders;
    private readonly HealthCheckService _healthChecks;

    public HomeController(IOrderCache cache, OrderService orders, HealthCheckService healthChecks)
    {
        _cache = cache;
        _orders = orders;
        _healthChecks = healthChecks;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        // Run the registered health checks in-process so the dashboard can render the same
        // results that /health returns, without an extra HTTP hop.
        ViewBag.Health = await _healthChecks.CheckHealthAsync(ct);

        var received = await _cache.GetReceivedAsync(10, ct);
        var processed = await _cache.GetProcessedAsync(10, ct);

        var pending = (await _orders.GetOrdersAsync(OrderStatus.Queued, ct)).Count
                      + (await _orders.GetOrdersAsync(OrderStatus.Processing, ct)).Count;

        return View(new DashboardViewModel
        {
            Counters = await _cache.GetCountersAsync(ct),
            RecentReceived = received,
            RecentProcessed = processed,
            CacheAvailable = _cache.IsAvailable,
            PendingInPipeline = pending
        });
    }

    public IActionResult Privacy() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
