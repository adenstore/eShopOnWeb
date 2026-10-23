using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.BasketService.Data;
using Microsoft.eShopWeb.BasketService.Data.Queries;
using Microsoft.eShopWeb.BasketService.Interfaces;
using Microsoft.eShopWeb.BasketService.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text.Json;
using Contracts;

// alias
using BasketDomainService = Microsoft.eShopWeb.BasketService.Services.BasketService;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<BasketDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("BasketConnection")));

builder.Services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
builder.Services.AddScoped(typeof(IAppLogger<>), typeof(LoggerAdapter<>));
builder.Services.AddScoped<IBasketService, BasketDomainService>();
builder.Services.AddScoped<IBasketQueryService, BasketQueryService>();

builder.Services.AddHealthChecks();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

ConnectionFactory factory = new ConnectionFactory();
app.Configuration.GetSection("RabbitMq").Bind(factory);
await using var connection = await factory.CreateConnectionAsync();

await using var channel = await connection.CreateChannelAsync(
    new CreateChannelOptions(publisherConfirmationsEnabled: true,
                             publisherConfirmationTrackingEnabled: true));

BasicProperties props = new BasicProperties { Persistent = true };

SemaphoreSlim publishLock = new SemaphoreSlim(1, 1);

string ExchangeName = "Basket";
string queueName = "basket-service";

string InvalidExchangeName = "Basket.invalid";
string InvalidQueueName = "invalid";

string DeadExchangeName = "Basket.unroutable";
string DeadQueueName    = "unroutable-messages";

Dictionary<string, object?> ExchangeArgs = new Dictionary<string, object?> {
    ["alternate-exchange"] = DeadExchangeName
};

Dictionary<string, object?> QueueArgs = new Dictionary<string, object?> {
    ["x-dead-letter-exchange"] = InvalidExchangeName
};

await channel.ExchangeDeclareAsync(exchange: ExchangeName, type: ExchangeType.Topic, durable: true, autoDelete: false, arguments: ExchangeArgs);

QueueDeclareOk queueDeclareResult = await channel.QueueDeclareAsync(queue: queueName, durable: true, exclusive: false, autoDelete: false, arguments: QueueArgs);
await channel.QueueBindAsync(queue: queueName, exchange: ExchangeName, routingKey: "Basket.addItem");

// invalid
await channel.ExchangeDeclareAsync(exchange: InvalidExchangeName, type: ExchangeType.Fanout, durable: true, autoDelete: false);
await channel.QueueDeclareAsync(queue: InvalidQueueName, durable: true, exclusive: false, autoDelete: false);
await channel.QueueBindAsync(queue: InvalidQueueName, exchange: InvalidExchangeName, routingKey: InvalidExchangeName);

// unroutable
await channel.ExchangeDeclareAsync(exchange: DeadExchangeName, type: ExchangeType.Fanout, durable: true, autoDelete: false);
await channel.QueueDeclareAsync(queue: DeadQueueName, durable: true, exclusive: false, autoDelete: false);
await channel.QueueBindAsync(queue: DeadQueueName, exchange: DeadExchangeName, routingKey: DeadExchangeName);

// One unacked message at a time. Without it the broker pushes the whole queue
// to this one consumer, and a crash redelivers all of it instead of one message.
await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false);

AsyncEventingBasicConsumer consumer = new AsyncEventingBasicConsumer(channel);
consumer.ReceivedAsync += async (model, ea) =>
{
    try {
        BasketRecord? b = JsonSerializer.Deserialize<BasketRecord>(ea.Body.Span);

        await channel.BasicAckAsync(deliveryTag: ea.DeliveryTag, multiple: false);
    }
    catch (Exception ex) {
        await channel.BasicNackAsync(deliveryTag: ea.DeliveryTag, multiple: false, requeue: false);
    }
};

await channel.BasicConsumeAsync(queueName, autoAck: false, consumer: consumer);

// Health check
app.MapHealthChecks("/health");

app.Logger.LogInformation("LAUNCHING BasketService");
app.Run();

public partial class Program { }
