using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHttpClient();
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

var orders = new List<Order>();
var nextOrderId = 1;

app.MapGet("/api/orders", () => Results.Ok(orders));

app.MapPost("/api/orders", async (CreateOrderRequest request, HttpClient httpClient) =>
{
    // 1. קריאה סינכרונית לניהול המלאי
    var reserveResponse = await httpClient.PostAsJsonAsync("http://inventoryservice:8080/api/inventory/reserve", new
    {
        productId = request.ProductId,
        quantity = request.Quantity
    });

    if (!reserveResponse.IsSuccessStatusCode)
    {
        return Results.BadRequest("Failed to reserve stock. Order rejected.");
    }

    var order = new Order
    {
        Id = nextOrderId++,
        ProductId = request.ProductId,
        Quantity = request.Quantity,
        Status = "Confirmed"
    };
    orders.Add(order);

    // 2. קריאה אסינכרונית ל-RabbitMQ עם מנגנון Retry
    var factory = new ConnectionFactory { HostName = "rabbitmq", Port = 5672 };
    IConnection? connection = null;
    
    for (int i = 1; i <= 5; i++)
    {
        try
        {
            connection = factory.CreateConnection();
            break;
        }
        catch (RabbitMQ.Client.Exceptions.BrokerUnreachableException)
        {
            if (i == 5) throw;
            await Task.Delay(5000);
        }
    }

    if (connection != null)
    {
        using var channel = connection.CreateModel();
        channel.QueueDeclare(queue: "order-notifications", durable: false, exclusive: false, autoDelete: false, arguments: null);

        var notificationPayload = new
        {
            OrderId = order.Id,
            Message = $"Order {order.Id} for product {order.ProductId} has been confirmed."
        };

        var json = JsonSerializer.Serialize(notificationPayload);
        var body = Encoding.UTF8.GetBytes(json);

        channel.BasicPublish(exchange: string.Empty, routingKey: "order-notifications", basicProperties: null, body: body);
    }

    return Results.Created($"/api/orders/{order.Id}", order);
});

app.Run();

public class Order
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public string Status { get; set; } = "Pending";
}

public class CreateOrderRequest
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}