using MassTransit;
using Microsoft.EntityFrameworkCore;
using OrderService.Data;
using OrderService.Models;
using Shared.Contracts;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
});

var messagingEnabled = builder.Configuration.GetValue("Messaging:Enabled", false);

var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

var connectionString = builder.Configuration.GetConnectionString("OrderDb")
    ?? "Server=sqlserver,1433;Database=OrderDb;User Id=sa;Password=YourStrong!Passw0rd;Encrypt=False;TrustServerCertificate=True;";

builder.Services.AddDbContext<OrderDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host("rabbitmq", "/", h => {
            h.Username("guest");
            h.Password("guest");
        });
    });
});
if (!messagingEnabled)
{
    builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.Use(async (context, next) =>
{
    var correlationId = CorrelationHelpers.GetCorrelationId(context);
    context.Items["CorrelationId"] = correlationId;
    context.Response.Headers["X-Correlation-ID"] = correlationId;

    using var scope = app.Logger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = correlationId });
    await next();
});

app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "OrderService" }));

app.MapGet("/api/orders", async (HttpContext httpContext, OrderDbContext db, ILoggerFactory loggerFactory) =>
{
    var correlationId = CorrelationHelpers.GetCorrelationId(httpContext);
    var logger = loggerFactory.CreateLogger("OrderService");
    logger.LogInformation("Fetching orders for correlation {CorrelationId}", correlationId);

    var orders = await db.Orders.OrderBy(o => o.CreatedAt).ToListAsync();
    return Results.Ok(orders);
});

app.MapPost("/api/orders", async (HttpContext httpContext, CreateOrderRequest request, OrderDbContext db, IPublishEndpoint publisher, ILoggerFactory loggerFactory) =>
{
    var correlationId = CorrelationHelpers.GetCorrelationId(httpContext);
    var logger = loggerFactory.CreateLogger("OrderService");
    logger.LogInformation("Creating order for correlation {CorrelationId} ProductId {ProductId} Quantity {Quantity}", correlationId, request.ProductId, request.Quantity);

    var order = new OrderEntity
    {
        ProductId = request.ProductId,
        Quantity = request.Quantity,
        Status = "Pending"
    };

    db.Orders.Add(order);
    await db.SaveChangesAsync();

    if (publisher is not null)
    {
        await publisher.Publish(new OrderCreatedEvent(order.Id, order.ProductId, order.Quantity, DateTime.UtcNow), context =>
        {
            context.Headers.Set("X-Correlation-ID", correlationId);
        });

        logger.LogInformation("Order {OrderId} published for correlation {CorrelationId}", order.Id, correlationId);
    }
    else
    {
        logger.LogWarning("Messaging is disabled; order {OrderId} was created without publishing for correlation {CorrelationId}", order.Id, correlationId);
    }

    return Results.Created($"/api/orders/{order.Id}", order);
});

using (var scope = app.Services.CreateScope())
{
    var orderDb = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    await orderDb.Database.EnsureCreatedAsync();
}

app.Run();

public class CreateOrderRequest
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}

public static class CorrelationHelpers
{
    public static string GetCorrelationId(HttpContext httpContext)
    {
        return httpContext.Request.Headers["X-Correlation-ID"].ToString()
            ?? httpContext.Items["CorrelationId"]?.ToString()
            ?? Guid.NewGuid().ToString("N");
    }
}

public class OrderStatusConsumer : IConsumer<InventoryReservedEvent>, IConsumer<InventoryReservationFailedEvent>
{
    private readonly OrderDbContext _db;
    private readonly ILogger<OrderStatusConsumer> _logger;

    public OrderStatusConsumer(OrderDbContext db, ILogger<OrderStatusConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<InventoryReservedEvent> context)
    {
        var correlationId = context.Headers.Get<string>("X-Correlation-ID") ?? Activity.Current?.TraceId.ToString() ?? "n/a";
        var order = await _db.Orders.FindAsync(context.Message.OrderId);
        if (order is null)
        {
            _logger.LogWarning("Order {OrderId} not found while handling reservation for correlation {CorrelationId}", context.Message.OrderId, correlationId);
            return;
        }

        order.Status = "Confirmed";
        await _db.SaveChangesAsync();
        _logger.LogInformation("Order {OrderId} confirmed for correlation {CorrelationId}", order.Id, correlationId);
    }

    public async Task Consume(ConsumeContext<InventoryReservationFailedEvent> context)
    {
        var correlationId = context.Headers.Get<string>("X-Correlation-ID") ?? Activity.Current?.TraceId.ToString() ?? "n/a";
        var order = await _db.Orders.FindAsync(context.Message.OrderId);
        if (order is null)
        {
            _logger.LogWarning("Order {OrderId} not found while handling failure for correlation {CorrelationId}", context.Message.OrderId, correlationId);
            return;
        }

        order.Status = "Rejected";
        await _db.SaveChangesAsync();
        _logger.LogInformation("Order {OrderId} rejected for correlation {CorrelationId} because {Reason}", order.Id, correlationId, context.Message.Reason);
    }
}