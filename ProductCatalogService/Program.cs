using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
});

var mongoConnectionString = builder.Configuration["MongoDb:ConnectionString"]
    ?? "mongodb://admin:admin123@mongodb:27017";
var databaseName = builder.Configuration["MongoDb:DatabaseName"] ?? "ProductCatalogDb";

builder.Services.AddSingleton<IMongoClient>(_ => new MongoClient(mongoConnectionString));
builder.Services.AddSingleton<IMongoDatabase>(sp =>
    sp.GetRequiredService<IMongoClient>().GetDatabase(databaseName));
builder.Services.AddSingleton<IMongoCollection<ProductDocument>>(sp =>
    sp.GetRequiredService<IMongoDatabase>().GetCollection<ProductDocument>("products"));

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

    using var scope = app.Logger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = correlationId });
    await next();
});

app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "ProductCatalogService" }));

app.MapGet("/api/products", async (HttpContext httpContext, IMongoCollection<ProductDocument> productsCollection, ILoggerFactory loggerFactory) =>
{
    var correlationId = CorrelationHelpers.GetCorrelationId(httpContext);
    var logger = loggerFactory.CreateLogger("ProductCatalogService");
    logger.LogInformation("Products requested for correlation {CorrelationId}", correlationId);

    var products = await productsCollection.Find(FilterDefinition<ProductDocument>.Empty).ToListAsync();
    return Results.Ok(products.Select(product => new ProductDto(product.Id ?? string.Empty, product.Name, product.Price, product.Stock)));
});

app.MapGet("/api/products/{id}", async (string id, IMongoCollection<ProductDocument> productsCollection) =>
{
    var product = await productsCollection.Find(p => p.Id == id).FirstOrDefaultAsync();
    return product is null ? Results.NotFound() : Results.Ok(new ProductDto(product.Id ?? string.Empty, product.Name, product.Price, product.Stock));
});

app.MapPost("/api/products", async (CreateProductRequest request, IMongoCollection<ProductDocument> productsCollection) =>
{
    var product = new ProductDocument
    {
        Name = request.Name,
        Price = request.Price,
        Stock = request.Stock
    };

    await productsCollection.InsertOneAsync(product);
    return Results.Created($"/api/products/{product.Id}", new ProductDto(product.Id ?? string.Empty, product.Name, product.Price, product.Stock));
});

using (var scope = app.Services.CreateScope())
{
    var productsCollection = scope.ServiceProvider.GetRequiredService<IMongoCollection<ProductDocument>>();
    var existingProducts = await productsCollection.CountDocumentsAsync(FilterDefinition<ProductDocument>.Empty);

    if (existingProducts == 0)
    {
        await productsCollection.InsertManyAsync(
        [
            new ProductDocument { Name = "Laptop", Price = 999.99m, Stock = 12 },
            new ProductDocument { Name = "Mouse", Price = 29.99m, Stock = 50 },
            new ProductDocument { Name = "Keyboard", Price = 79.99m, Stock = 30 }
        ]);
    }
}

app.Run();

public class ProductDocument
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
