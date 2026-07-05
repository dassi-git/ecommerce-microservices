using MassTransit;
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

builder.Services.AddMassTransit(x =>
{
    x.SetKebabCaseEndpointNameFormatter();
    x.AddConsumer<NotificationConsumer>();
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

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers["X-Correlation-ID"].ToString()
        ?? context.Items["CorrelationId"]?.ToString()
        ?? Guid.NewGuid().ToString("N");

    context.Items["CorrelationId"] = correlationId;
    context.Response.Headers["X-Correlation-ID"] = correlationId;

    using var scope = app.Logger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = correlationId });
    await next();
});

app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "NotificationService" }));

app.Run();

public class NotificationConsumer : IConsumer<OrderCreatedEvent>, IConsumer<InventoryReservedEvent>, IConsumer<InventoryReservationFailedEvent>
{
    private readonly ILogger<NotificationConsumer> _logger;

    public NotificationConsumer(ILogger<NotificationConsumer> logger)
    {
        _logger = logger;
    }

    public Task Consume(ConsumeContext<OrderCreatedEvent> context)
    {
        var correlationId = context.Headers.Get<string>("X-Correlation-ID") ?? Activity.Current?.TraceId.ToString() ?? "n/a";
        using var scope = _logger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = correlationId });
        _logger.LogInformation("Notification event OrderCreatedEvent CorrelationId {CorrelationId} OrderId {OrderId} ProductId {ProductId} Quantity {Quantity}", correlationId, context.Message.OrderId, context.Message.ProductId, context.Message.Quantity);
        return Task.CompletedTask;
    }

    public Task Consume(ConsumeContext<InventoryReservedEvent> context)
    {
        var correlationId = context.Headers.Get<string>("X-Correlation-ID") ?? Activity.Current?.TraceId.ToString() ?? "n/a";
        using var scope = _logger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = correlationId });
        _logger.LogInformation("Notification event InventoryReservedEvent CorrelationId {CorrelationId} OrderId {OrderId} ProductId {ProductId} Quantity {Quantity}", correlationId, context.Message.OrderId, context.Message.ProductId, context.Message.Quantity);
        return Task.CompletedTask;
    }

    public Task Consume(ConsumeContext<InventoryReservationFailedEvent> context)
    {
        var correlationId = context.Headers.Get<string>("X-Correlation-ID") ?? Activity.Current?.TraceId.ToString() ?? "n/a";
        using var scope = _logger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = correlationId });
        _logger.LogWarning("Notification event InventoryReservationFailedEvent CorrelationId {CorrelationId} OrderId {OrderId} ProductId {ProductId} Quantity {Quantity} Reason {Reason}", correlationId, context.Message.OrderId, context.Message.ProductId, context.Message.Quantity, context.Message.Reason);
        return Task.CompletedTask;
    }
}