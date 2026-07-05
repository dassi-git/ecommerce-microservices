using InventoryService.Data;
using InventoryService.Models;
using MassTransit;
using Microsoft.EntityFrameworkCore;
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

var connectionString = builder.Configuration.GetConnectionString("InventoryDb")
    ?? "Server=sqlserver,1433;Database=InventoryDb;User Id=sa;Password=YourStrong!Passw0rd;Encrypt=False;TrustServerCertificate=True;";

builder.Services.AddDbContext<InventoryDbContext>(options =>
    options.UseSqlServer(connectionString));

if (messagingEnabled)
{
    builder.Services.AddMassTransit(x =>
    {
        x.SetKebabCaseEndpointNameFormatter();
        x.AddConsumer<InventoryReservationConsumer>();
        x.UsingRabbitMq((context, cfg) =>
        {
            cfg.Host("rabbitmq", "/", h =>
            {
                h.Username("guest");
                h.Password("guest");
            });
            cfg.ConfigureEndpoints(context);
        });
    });
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

app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "InventoryService" }));

app.MapGet("/api/inventory", async (HttpContext httpContext, InventoryDbContext db, ILoggerFactory loggerFactory) =>
{
    var correlationId = CorrelationHelpers.GetCorrelationId(httpContext);
    var logger = loggerFactory.CreateLogger("InventoryService");
    logger.LogInformation("Inventory requested for correlation {CorrelationId}", correlationId);

    var inventory = await db.InventoryItems.OrderBy(i => i.ProductId).ToListAsync();
    return Results.Ok(inventory);
});

using (var scope = app.Services.CreateScope())
{
    var inventoryDb = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    await inventoryDb.Database.EnsureCreatedAsync();

    if (!await inventoryDb.InventoryItems.AnyAsync())
    {
        inventoryDb.InventoryItems.AddRange(
        [
            new InventoryItemEntity { ProductId = 1, Name = "Laptop", Stock = 12 },
            new InventoryItemEntity { ProductId = 2, Name = "Mouse", Stock = 50 },
            new InventoryItemEntity { ProductId = 3, Name = "Keyboard", Stock = 30 }
        ]);
        await inventoryDb.SaveChangesAsync();
    }
}

app.Run();

public static class CorrelationHelpers
{
    public static string GetCorrelationId(HttpContext httpContext)
    {
        return httpContext.Request.Headers["X-Correlation-ID"].ToString()
            ?? httpContext.Items["CorrelationId"]?.ToString()
            ?? Guid.NewGuid().ToString("N");
    }
}

public class InventoryReservationConsumer : IConsumer<OrderCreatedEvent>
{
    private readonly InventoryDbContext _db;
    private readonly IPublishEndpoint _publisher;
    private readonly ILogger<InventoryReservationConsumer> _logger;

    public InventoryReservationConsumer(InventoryDbContext db, IPublishEndpoint publisher, ILogger<InventoryReservationConsumer> logger)
    {
        _db = db;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<OrderCreatedEvent> context)
    {
        var correlationId = context.Headers.Get<string>("X-Correlation-ID") ?? Activity.Current?.TraceId.ToString() ?? "n/a";
        var item = await _db.InventoryItems.FirstOrDefaultAsync(i => i.ProductId == context.Message.ProductId);
        if (item is null || item.Stock < context.Message.Quantity)
        {
            _logger.LogWarning("Inventory reservation failed for order {OrderId} correlation {CorrelationId}", context.Message.OrderId, correlationId);
            await _publisher.Publish(new InventoryReservationFailedEvent(context.Message.OrderId, context.Message.ProductId, context.Message.Quantity, "Insufficient stock", DateTime.UtcNow), messageContext =>
            {
                messageContext.Headers.Set("X-Correlation-ID", correlationId);
            });
            return;
        }

        item.Stock -= context.Message.Quantity;
        await _db.SaveChangesAsync();
        _logger.LogInformation("Inventory reserved for order {OrderId} correlation {CorrelationId}", context.Message.OrderId, correlationId);
        await _publisher.Publish(new InventoryReservedEvent(context.Message.OrderId, context.Message.ProductId, context.Message.Quantity, DateTime.UtcNow), messageContext =>
        {
            messageContext.Headers.Set("X-Correlation-ID", correlationId);
        });
    }
}