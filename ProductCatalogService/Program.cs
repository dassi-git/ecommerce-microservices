using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "ProductCatalogService")
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Logging.ClearProviders();
builder.Logging.AddSerilog();

var mongoConnectionString = builder.Configuration.GetConnectionString("MongoConnection")
    ?? builder.Configuration["MongoDb:ConnectionString"]
    ?? "mongodb://admin:admin123@productcatalogmongodb:27017";
var databaseName = builder.Configuration["MongoDb:DatabaseName"] ?? "ProductCatalogDb";

builder.Services.AddSingleton<IMongoClient>(_ => new MongoClient(mongoConnectionString));
builder.Services.AddSingleton<IMongoDatabase>(sp =>
    sp.GetRequiredService<IMongoClient>().GetDatabase(databaseName));
builder.Services.AddSingleton<IMongoCollection<Product>>(sp =>
    sp.GetRequiredService<IMongoDatabase>().GetCollection<Product>("products"));
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis") ?? "redis:6379";
    options.InstanceName = "productcatalog:";
});

var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
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
    context.Response.Headers["X-Container-ID"] = Environment.MachineName;

    using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
    {
        using var scope = app.Logger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = correlationId });
        await next();
    }
});

app.MapHealthChecks("/health");

app.MapGet("/api/products", async (HttpContext httpContext, IMongoCollection<Product> productsCollection, ILoggerFactory loggerFactory) =>
{
    var correlationId = CorrelationHelpers.GetCorrelationId(httpContext);
    var logger = loggerFactory.CreateLogger("ProductCatalogService");
    logger.LogInformation("Products requested for correlation {CorrelationId}", correlationId);

    var products = await productsCollection.Find(FilterDefinition<Product>.Empty).ToListAsync();
    return Results.Ok(products.Select(product => new ProductDto(product.Id ?? string.Empty, product.Name, product.Price, product.Stock)));
});

app.MapGet("/api/products/{id}", async (string id, IMongoCollection<Product> productsCollection, IDistributedCache cache) =>
{
    var cacheKey = $"product:{id}";
    var cachedValue = await cache.GetStringAsync(cacheKey);

    if (!string.IsNullOrEmpty(cachedValue))
    {
        var cachedProduct = JsonSerializer.Deserialize<ProductDto>(cachedValue);
        if (cachedProduct is not null)
        {
            return Results.Ok(cachedProduct);
        }
    }

    var product = await productsCollection.Find(p => p.Id == id).FirstOrDefaultAsync();
    if (product is null)
    {
        return Results.NotFound();
    }

    var productDto = new ProductDto(product.Id ?? string.Empty, product.Name, product.Price, product.Stock);
    var payload = JsonSerializer.Serialize(productDto);
    await cache.SetStringAsync(cacheKey, payload, new DistributedCacheEntryOptions
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
    });

    return Results.Ok(productDto);
});

app.MapPost("/api/products", async (CreateProductRequest request, IMongoCollection<Product> productsCollection, IDistributedCache cache) =>
{
    var product = new Product
    {
        Name = request.Name,
        Price = request.Price,
        Stock = request.Stock
    };

    await productsCollection.InsertOneAsync(product);
    var productDto = new ProductDto(product.Id ?? string.Empty, product.Name, product.Price, product.Stock);

    await cache.RemoveAsync($"product:{product.Id}");

    return Results.Created($"/api/products/{product.Id}", productDto);
});

using (var scope = app.Services.CreateScope())
{
    var productsCollection = scope.ServiceProvider.GetRequiredService<IMongoCollection<Product>>();
    var existingProducts = await productsCollection.CountDocumentsAsync(FilterDefinition<Product>.Empty);

    if (existingProducts == 0)
    {
        await productsCollection.InsertManyAsync(
        [
            new Product { Name = "Laptop", Price = 999.99m, Stock = 12 },
            new Product { Name = "Mouse", Price = 29.99m, Stock = 50 },
            new Product { Name = "Keyboard", Price = 79.99m, Stock = 30 }
        ]);
    }
}

app.Run();

public class Product
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Stock { get; set; }
}

public record ProductDto(string Id, string Name, decimal Price, int Stock);

public class CreateProductRequest
{
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Stock { get; set; }
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
