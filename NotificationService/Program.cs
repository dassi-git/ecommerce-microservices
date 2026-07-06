using MassTransit;
using Prometheus;
using Serilog;
using Shared.Contracts;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "NotificationService")
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Logging.ClearProviders();
builder.Logging.AddSerilog();

var messagingEnabled = builder.Configuration.GetValue("Messaging:Enabled", true);

var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

builder.Services.AddMassTransit(x =>
{
    x.SetKebabCaseEndpointNameFormatter();
    x.AddConsumer<OrderPlacedConsumer>();
    x.AddConsumer<InventoryReservedConsumer>();
    x.AddConsumer<InventoryRejectedConsumer>();
    x.UsingRabbitMq((context, cfg) =>
    {
        var rabbitHost = builder.Configuration["RabbitMQ:Host"]
            ?? builder.Configuration["RabbitMq:Host"]
            ?? "rabbitmq";
        var rabbitUsername = builder.Configuration["RabbitMQ:Username"] ?? "guest";
        var rabbitPassword = builder.Configuration["RabbitMQ:Password"] ?? "guest";

        cfg.Host(rabbitHost, "/", h =>
        {
            h.Username(rabbitUsername);
            h.Password(rabbitPassword);
        });
        cfg.ConfigureEndpoints(context);
    });
});

var app = builder.Build();

app.UseMetricServer();

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

    using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
    {
        using var scope = app.Logger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = correlationId });
        await next();
    }
});

app.MapHealthChecks("/health");

app.Run();

public class OrderPlacedConsumer : IConsumer<OrderPlacedEvent>
{
    private readonly ILogger<OrderPlacedConsumer> _logger;

    public OrderPlacedConsumer(ILogger<OrderPlacedConsumer> logger)
    {
        _logger = logger;
    }

    public Task Consume(ConsumeContext<OrderPlacedEvent> context)
    {
        var correlationId = context.Headers.Get<string>("X-Correlation-ID") ?? Activity.Current?.TraceId.ToString() ?? "n/a";
        using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
        {
            _logger.LogInformation("Notification Sent: Order {OrderId} accepted and queued for processing for correlation {CorrelationId}", context.Message.OrderId, correlationId);
        }

        return Task.CompletedTask;
    }
}

public class InventoryReservedConsumer : IConsumer<InventoryReservedEvent>
{
    private readonly ILogger<InventoryReservedConsumer> _logger;

    public InventoryReservedConsumer(ILogger<InventoryReservedConsumer> logger)
    {
        _logger = logger;
    }

    public Task Consume(ConsumeContext<InventoryReservedEvent> context)
    {
        var correlationId = context.Headers.Get<string>("X-Correlation-ID") ?? Activity.Current?.TraceId.ToString() ?? "n/a";
        using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
        {
            _logger.LogInformation("Notification Sent: Order {OrderId} was confirmed and inventory was reserved for correlation {CorrelationId}", context.Message.OrderId, correlationId);
        }

        return Task.CompletedTask;
    }
}

public class InventoryRejectedConsumer : IConsumer<InventoryRejectedEvent>
{
    private readonly ILogger<InventoryRejectedConsumer> _logger;

    public InventoryRejectedConsumer(ILogger<InventoryRejectedConsumer> logger)
    {
        _logger = logger;
    }

    public Task Consume(ConsumeContext<InventoryRejectedEvent> context)
    {
        var correlationId = context.Headers.Get<string>("X-Correlation-ID") ?? Activity.Current?.TraceId.ToString() ?? "n/a";
        using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
        {
            _logger.LogWarning("Notification Sent: Order {OrderId} was rejected because {Reason} for correlation {CorrelationId}", context.Message.OrderId, context.Message.Reason, correlationId);
        }

        return Task.CompletedTask;
    }
}