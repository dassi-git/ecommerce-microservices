using System.Net.Http.Json;
using Microsoft.Extensions.Primitives;
using Prometheus;
using Serilog;
using Serilog.Context;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "ApiGateway")
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<CorrelationForwardingHandler>();
builder.Services.AddHttpClient("downstream")
    .AddHttpMessageHandler<CorrelationForwardingHandler>();
builder.Logging.ClearProviders();
builder.Logging.AddSerilog();
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.UseMetricServer();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers.TryGetValue("X-Correlation-ID", out StringValues value)
        ? value.ToString()
        : Guid.NewGuid().ToString("N");

    context.Items["CorrelationId"] = correlationId;
    context.Response.Headers["X-Correlation-ID"] = correlationId;
    context.Request.Headers["X-Correlation-ID"] = correlationId;

    using (LogContext.PushProperty("CorrelationId", correlationId))
    {
        using var scope = app.Logger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = correlationId });
        await next();
    }
});

app.MapHealthChecks("/health");

app.MapGet("/api/bff/dashboard", async (HttpContext httpContext, IHttpClientFactory httpClientFactory) =>
{
    var correlationId = httpContext.Items["CorrelationId"]?.ToString()
        ?? httpContext.Request.Headers["X-Correlation-ID"].ToString()
        ?? Guid.NewGuid().ToString("N");

    var client = httpClientFactory.CreateClient("downstream");
    using var ordersRequest = new HttpRequestMessage(HttpMethod.Get, "http://orderservice:8080/api/orders");
    ordersRequest.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);

    using var inventoryRequest = new HttpRequestMessage(HttpMethod.Get, "http://inventoryservice:8080/api/inventory");
    inventoryRequest.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);

    var ordersTask = client.SendAsync(ordersRequest);
    var inventoryTask = client.SendAsync(inventoryRequest);

    var ordersResponse = await ordersTask;
    ordersResponse.EnsureSuccessStatusCode();
    var inventoryResponse = await inventoryTask;
    inventoryResponse.EnsureSuccessStatusCode();

    var activeOrders = await ordersResponse.Content.ReadFromJsonAsync<List<OrderSummary>>();
    var inventoryItems = await inventoryResponse.Content.ReadFromJsonAsync<List<InventorySummary>>();

    app.Logger.LogInformation("BFF dashboard prepared for correlation {CorrelationId}", correlationId);

    return Results.Ok(new
    {
        GeneratedAt = DateTime.UtcNow,
        CorrelationId = correlationId,
        ActiveOrders = activeOrders ?? [],
        InventoryItems = inventoryItems ?? []
    });
});

app.MapGet("/api/bff/order-details/{id:int}", async (int id, HttpContext httpContext, IHttpClientFactory httpClientFactory) =>
{
    var correlationId = httpContext.Items["CorrelationId"]?.ToString()
        ?? httpContext.Request.Headers["X-Correlation-ID"].ToString()
        ?? Guid.NewGuid().ToString("N");

    var client = httpClientFactory.CreateClient("downstream");

    using var orderRequest = new HttpRequestMessage(HttpMethod.Get, $"http://orderservice:8080/api/orders/{id}");
    orderRequest.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);

    var orderResponse = await client.SendAsync(orderRequest);
    if (!orderResponse.IsSuccessStatusCode)
    {
        return Results.NotFound(new {message = $"Order {id} was not found." });
    }

    var order = await orderResponse.Content.ReadFromJsonAsync<OrderSummary>();
    if (order is null)
    {
        return Results.NotFound(new { message = $"Order {id} was not found." });
    }

    using var productRequest = new HttpRequestMessage(HttpMethod.Get, $"http://productcatalogservice:8080/api/products/{order.ProductId}");
    productRequest.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);

    var productResponse = await client.SendAsync(productRequest);
    if (!productResponse.IsSuccessStatusCode)
    {
        return Results.Ok(new
        {
            CorrelationId = correlationId,
            Order = order,
            Product = (object?)null
        });
    }

    var product = await productResponse.Content.ReadFromJsonAsync<ProductSummary>();

    app.Logger.LogInformation("BFF order details prepared for order {OrderId} correlation {CorrelationId}", id, correlationId);

    return Results.Ok(new
    {
        CorrelationId = correlationId,
        Order = order,
        Product = product
    });
});

app.MapReverseProxy();

app.Run();

public record OrderSummary(int Id, int ProductId, int Quantity, string Status, DateTime CreatedAt);
public record InventorySummary(int ProductId, string Name, int Stock);
public record ProductSummary(string Id, string Name, decimal Price, int Stock);

public sealed class CorrelationForwardingHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CorrelationForwardingHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var correlationId = httpContext?.Items["CorrelationId"]?.ToString()
            ?? httpContext?.Request.Headers["X-Correlation-ID"].ToString()
            ?? Guid.NewGuid().ToString("N");

        if (!request.Headers.Contains("X-Correlation-ID"))
        {
            request.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);
        }

        if (httpContext is not null)
        {
            request.Options.Set(new HttpRequestOptionsKey<string>("CorrelationId"), correlationId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
