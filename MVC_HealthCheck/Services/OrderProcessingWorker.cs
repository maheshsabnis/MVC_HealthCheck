using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MVC_HealthCheck.Data;
using MVC_HealthCheck.Messaging;
using MVC_HealthCheck.Models;
using MVC_HealthCheck.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace MVC_HealthCheck.Services;

/// <summary>
/// Consumes orders from RabbitMQ, applies the advance rule, writes the outcome to PostgreSQL,
/// updates the SQL Server order status and pushes the result onto the Redis activity list.
///
/// The rule: an order is Accepted when the advance is at least
/// <see cref="OrderProcessingOptions.MinimumAdvancePercentage"/> (50% by default) of the order
/// total. Anything below that is Rejected.
/// </summary>
public sealed class OrderProcessingWorker : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    private readonly RabbitMqConnectionProvider _connections;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OrderProcessingOptions _options;
    private readonly ILogger<OrderProcessingWorker> _logger;

    public OrderProcessingWorker(
        RabbitMqConnectionProvider connections,
        IServiceScopeFactory scopeFactory,
        IOptions<OrderProcessingOptions> options,
        ILogger<OrderProcessingWorker> logger)
    {
        _connections = connections;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Keep trying to attach to the broker. If RabbitMQ is down at boot the app still starts
        // and the rabbitmq health check reports the outage.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Order consumer dropped out. Retrying in {Delay}s.", ReconnectDelay.TotalSeconds);
                try
                {
                    await Task.Delay(ReconnectDelay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        var connection = await _connections.GetConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await _connections.DeclareQueueAsync(channel, stoppingToken);

        // One unacknowledged message at a time keeps the UI transitions easy to follow.
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, args) => await HandleDeliveryAsync(channel, args, stoppingToken);

        await channel.BasicConsumeAsync(
            queue: _connections.QueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation("Order processing worker is consuming from {Queue}.", _connections.QueueName);

        // Park here until shutdown; the consumer callbacks do the work.
        var idle = new TaskCompletionSource();
        await using (stoppingToken.Register(() => idle.TrySetResult()))
        {
            await idle.Task;
        }
    }

    private async Task HandleDeliveryAsync(IChannel channel, BasicDeliverEventArgs args, CancellationToken ct)
    {
        OrderMessage? message = null;

        try
        {
            var json = Encoding.UTF8.GetString(args.Body.Span);
            message = JsonSerializer.Deserialize<OrderMessage>(json, JsonOptions);

            if (message is null)
            {
                _logger.LogWarning("Discarding an unreadable message (delivery tag {Tag}).", args.DeliveryTag);
                await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false, cancellationToken: ct);
                return;
            }

            await ProcessAsync(message, ct);
            await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process order {OrderNumber}.", message?.OrderNumber ?? "unknown");

            if (message is not null)
            {
                await MarkFailedAsync(message, ex, ct);
            }

            // Do not requeue: a poison message would otherwise loop forever. The order is left
            // in the Failed state and stays visible on the Order Status screen.
            await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false, cancellationToken: ct);
        }
    }

    private async Task ProcessAsync(OrderMessage message, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        using var scope = _scopeFactory.CreateScope();
        var sql = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var pg = scope.ServiceProvider.GetRequiredService<ProcessedOrdersDbContext>();
        var cache = scope.ServiceProvider.GetRequiredService<IOrderCache>();

        var order = await sql.Orders.FirstOrDefaultAsync(o => o.Id == message.OrderId, ct);
        if (order is null)
        {
            _logger.LogWarning("Order {OrderId} is no longer in SQL Server. Dropping the message.", message.OrderId);
            return;
        }

        order.Status = OrderStatus.Processing;
        await sql.SaveChangesAsync(ct);

        if (_options.SimulatedWorkMilliseconds > 0)
        {
            await Task.Delay(_options.SimulatedWorkMilliseconds, ct);
        }

        // --- the business rule ---------------------------------------------------------------
        var advancePercentage = message.TotalAmount <= 0
            ? 0m
            : Math.Round(message.AdvanceAmount / message.TotalAmount * 100m, 2);

        var accepted = advancePercentage >= _options.MinimumAdvancePercentage;

        var reason = accepted
            ? $"Advance of {advancePercentage:0.##}% meets the required minimum of {_options.MinimumAdvancePercentage:0.##}%."
            : $"Advance of {advancePercentage:0.##}% is below the required minimum of {_options.MinimumAdvancePercentage:0.##}%.";
        // ---------------------------------------------------------------------------------------

        stopwatch.Stop();
        var processedOnUtc = DateTime.UtcNow;

        // Idempotent: a redelivery updates the existing row rather than inserting a duplicate.
        var processed = await pg.ProcessedOrders.FirstOrDefaultAsync(p => p.OrderId == message.OrderId, ct);
        var isNew = processed is null;
        processed ??= new ProcessedOrder { OrderId = message.OrderId };

        processed.OrderNumber = message.OrderNumber;
        processed.CustomerId = message.CustomerId;
        processed.CustomerName = message.CustomerName;
        processed.TotalAmount = message.TotalAmount;
        processed.AdvanceAmount = message.AdvanceAmount;
        processed.AdvancePercentage = advancePercentage;
        processed.Decision = accepted ? "Accepted" : "Rejected";
        processed.Reason = reason;
        processed.ItemCount = message.ItemCount;
        processed.OrderPlacedOnUtc = DateTime.SpecifyKind(message.OrderPlacedOnUtc, DateTimeKind.Utc);
        processed.ProcessedOnUtc = processedOnUtc;
        processed.ProcessingDurationMs = stopwatch.ElapsedMilliseconds;

        if (isNew)
        {
            pg.ProcessedOrders.Add(processed);
        }

        await pg.SaveChangesAsync(ct);

        order.Status = accepted ? OrderStatus.Accepted : OrderStatus.Rejected;
        order.StatusMessage = reason;
        order.ProcessedOn = processedOnUtc;
        await sql.SaveChangesAsync(ct);

        await cache.RecordProcessedAsync(new OrderCacheEntry
        {
            OrderId = message.OrderId,
            OrderNumber = message.OrderNumber,
            CustomerName = message.CustomerName,
            TotalAmount = message.TotalAmount,
            AdvanceAmount = message.AdvanceAmount,
            AdvancePercentage = advancePercentage,
            Status = processed.Decision,
            Reason = reason,
            TimestampUtc = processedOnUtc
        }, ct);

        _logger.LogInformation("Order {OrderNumber} {Decision} ({Percentage:0.##}% advance).",
            message.OrderNumber, processed.Decision, advancePercentage);
    }

    private async Task MarkFailedAsync(OrderMessage message, Exception cause, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var sql = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

            var order = await sql.Orders.FirstOrDefaultAsync(o => o.Id == message.OrderId, ct);
            if (order is null) return;

            order.Status = OrderStatus.Failed;
            order.StatusMessage = $"Processing failed: {cause.Message}";
            await sql.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not mark order {OrderNumber} as failed.", message.OrderNumber);
        }
    }
}
