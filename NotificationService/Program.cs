using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

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

// הפעלת הצרכן (Consumer) ברקע עם מנגנון Retry לחיבור ל-RabbitMQ
Task.Run(async () =>
{
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
            if (i == 5)
            {
                Console.WriteLine("NotificationService could not connect to RabbitMQ after 5 attempts.");
                return;
            }
            await Task.Delay(5000);
        }
    }

    if (connection != null)
    {
        var channel = connection.CreateModel();
        channel.QueueDeclare(queue: "order-notifications", durable: false, exclusive: false, autoDelete: false, arguments: null);

        var consumer = new EventingBasicConsumer(channel);
        consumer.Received += (model, ea) =>
        {
            var body = ea.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);
            Console.WriteLine($"Received notification message: {message}");
            channel.BasicAck(ea.DeliveryTag, false);
        };

        channel.BasicConsume(queue: "order-notifications", autoAck: false, consumer: consumer);
    }
});

app.Run();