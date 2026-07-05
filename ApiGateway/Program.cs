using System.Net.Http.Json;
using Microsoft.Extensions.Primitives;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient("downstream")
    .AddHttpMessageHandler<CorrelationForwardingHandler>();
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
});
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

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

    using var scope = app.Logger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = correlationId });
    await next();
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

    var ordersResponse = await client.SendAsync(ordersRequest);
    ordersResponse.EnsureSuccessStatusCode();
    var inventoryResponse = await client.SendAsync(inventoryRequest);
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

app.MapReverseProxy();

app.Run();

public record OrderSummary(int Id, int ProductId, int Quantity, string Status, DateTime CreatedAt);
public record InventorySummary(int ProductId, string Name, int Stock);

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

        return base.SendAsync(request, cancellationToken);
    }
}
