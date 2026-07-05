using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddStackExchangeRedisCache(options => options.Configuration = "redis:6379");

var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/api/products", async (IDistributedCache cache) =>
{
    const string cacheKey = "products:list";
    var cachedProducts = await cache.GetStringAsync(cacheKey);

    if (!string.IsNullOrEmpty(cachedProducts))
    {
        return Results.Ok(JsonSerializer.Deserialize<List<ProductDto>>(cachedProducts));
    }

    var products = new List<ProductDto>
    {
        new(1, "Laptop", 999.99m, 12),
        new(2, "Mouse", 29.99m, 50),
        new(3, "Keyboard", 79.99m, 30)
    };

    var serializedProducts = JsonSerializer.Serialize(products);
    await cache.SetStringAsync(cacheKey, serializedProducts, new DistributedCacheEntryOptions
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
    });

    return Results.Ok(products);
});

app.MapHealthChecks("/health");

app.Run();

public record ProductDto(int Id, string Name, decimal Price, int Stock);
