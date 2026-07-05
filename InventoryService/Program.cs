using System.Net.Http.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks(); // הוספת בדיקת בריאות

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

app.MapHealthChecks("/health"); // ניתוב בדיקת בריאות

var inventory = new List<InventoryItem>
{
    new InventoryItem { ProductId = 1, Name = "Laptop", Stock = 12 },
    new InventoryItem { ProductId = 2, Name = "Mouse", Stock = 50 },
    new InventoryItem { ProductId = 3, Name = "Keyboard", Stock = 30 }
};

app.MapGet("/api/inventory", () => Results.Ok(inventory));

app.MapPost("/api/inventory/reserve", (ReserveRequest request) =>
{
    var item = inventory.FirstOrDefault(i => i.ProductId == request.ProductId);
    if (item == null || item.Stock < request.Quantity)
    {
        return Results.BadRequest("Stock unavailable");
    }

    item.Stock -= request.Quantity;
    return Results.Ok(new { Message = "Stock reserved successfully", UpdatedStock = item.Stock });
});

app.Run();

public class InventoryItem
{
    public int ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Stock { get; set; }
}

public class ReserveRequest
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}