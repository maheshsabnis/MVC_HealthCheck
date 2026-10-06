using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using MVC_HealthCheck.Models;
using MVC_HealthCheck.Services;
using MVC_HealthCheck.ViewModels;

namespace MVC_HealthCheck.Controllers;

public class OrdersController : Controller
{
    private readonly OrderService _orders;
    private readonly ProcessedOrderService _processed;
    private readonly IOrderCache _cache;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(
        OrderService orders,
        ProcessedOrderService processed,
        IOrderCache cache,
        ILogger<OrdersController> logger)
    {
        _orders = orders;
        _processed = processed;
        _cache = cache;
        _logger = logger;
    }

    // ---------------------------------------------------------------- Create Orders ----------

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var model = new CreateOrderViewModel();
        await PopulateLookupsAsync(model, ct);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateOrderViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            await PopulateLookupsAsync(model, ct);
            return View(model);
        }

        try
        {
            var order = await _orders.CreateOrderAsync(model, ct);

            TempData["Flash"] = order.Status == OrderStatus.Failed
                ? $"Order {order.OrderNumber} was saved but could not be queued. See its status for details."
                : $"Order {order.OrderNumber} was created and queued for processing.";
            TempData["FlashKind"] = order.Status == OrderStatus.Failed ? "warning" : "success";

            return RedirectToAction(nameof(Status));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Order creation failed.");
            ModelState.AddModelError(string.Empty, ex.Message);
            await PopulateLookupsAsync(model, ct);
            return View(model);
        }
    }

    // ------------------------------------------------------------- Checking Order Status -----

    [HttpGet]
    public async Task<IActionResult> Status(OrderStatus? status, CancellationToken ct)
    {
        var model = new OrderStatusViewModel
        {
            Orders = await _orders.GetOrdersAsync(status, ct),
            Filter = status,
            Counters = await _cache.GetCountersAsync(ct),
            CacheAvailable = _cache.IsAvailable
        };

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var order = await _orders.GetOrderAsync(id, ct);
        if (order is null)
        {
            return NotFound();
        }

        ViewBag.Processed = await _processed.GetByOrderIdAsync(id, ct);
        return View(order);
    }

    // ------------------------------------------------------------ Viewing Processed Orders ---

    [HttpGet]
    public async Task<IActionResult> Processed(CancellationToken ct)
    {
        var model = await _processed.GetAsync(decision: null, ct);
        return View(model);
    }

    // ------------------------------------------------------- Viewing Accepted and Rejected ---

    [HttpGet]
    public async Task<IActionResult> Decisions(string? decision, CancellationToken ct)
    {
        // Guard the filter so an arbitrary query string cannot reach the database.
        var normalised = decision?.Trim().ToLowerInvariant() switch
        {
            "accepted" => "Accepted",
            "rejected" => "Rejected",
            _ => null
        };

        var model = await _processed.GetAsync(normalised, ct);
        return View(model);
    }

    private async Task PopulateLookupsAsync(CreateOrderViewModel model, CancellationToken ct)
    {
        var customers = await _orders.GetCustomersAsync(ct);
        model.CustomerOptions = customers.Select(c => new SelectListItem
        {
            Value = c.Id.ToString(),
            Text = $"{c.Name} ({c.City})",
            Selected = c.Id == model.CustomerId
        });

        model.Products = await _orders.GetProductsAsync(ct);

        if (model.Items.Count == 0)
        {
            model.Items.Add(new OrderLineInput());
        }
    }
}
