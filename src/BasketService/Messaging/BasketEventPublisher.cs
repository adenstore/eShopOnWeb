using System.Text.Json;
using Microsoft.eShopWeb.BasketService.Contracts;
using RabbitMQ.Client;

namespace Microsoft.eShopWeb.BasketService.Messaging;

public interface IBasketEventPublisher
{
    Task PublishBasketChangedAsync(BasketSnapshot basket, CancellationToken cancellationToken = default);
}

/// <summary>
/// Publishes <see cref="BasketChangedIntegrationEvent"/> to the "basket-events" fanout exchange.
/// Uses its own short-lived channel per message rather than the consumer's channel: publishing on
/// the channel you are mid-delivery on is the kind of re-entrancy that bites later, and channels
/// are cheap.
/// </summary>
public sealed class RabbitMqBasketEventPublisher : IBasketEventPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IRabbitMqConnection _connection;
    private readonly ILogger<RabbitMqBasketEventPublisher> _logger;

    public RabbitMqBasketEventPublisher(IRabbitMqConnection connection, ILogger<RabbitMqBasketEventPublisher> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    public async Task PublishBasketChangedAsync(BasketSnapshot basket, CancellationToken cancellationToken = default)
    {
        var basketChanged = BasketChangedIntegrationEvent.For(basket);
        try
        {
            var connection = await _connection.GetAsync(cancellationToken);
            await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

            // Declaring is idempotent: creates the exchange the first time, no-op afterwards.
            await channel.ExchangeDeclareAsync(BasketMessaging.EventsExchange, ExchangeType.Fanout,
                durable: true, cancellationToken: cancellationToken);

            var properties = new BasicProperties
            {
                ContentType = "application/json",
                Type = nameof(BasketChangedIntegrationEvent),
                Persistent = true
            };

            await channel.BasicPublishAsync(BasketMessaging.EventsExchange, routingKey: string.Empty,
                mandatory: false, basicProperties: properties,
                body: JsonSerializer.SerializeToUtf8Bytes(basketChanged, JsonOptions),
                cancellationToken: cancellationToken);

            _logger.LogInformation("Published BasketChanged for basket {BasketId} ({ItemCount} items)",
                basket.BasketId, basket.Items.Count);
        }
        catch (Exception ex)
        {
            // The basket change is already committed, so the command must not be retried: replaying
            // AddItem would add the quantity a second time. The cost of swallowing this is that any
            // projection stays stale until the next change to this basket — an outbox table would
            // close that gap, and is the obvious next step if the staleness ever shows up in a demo.
            _logger.LogError(ex, "Could not publish BasketChanged for basket {BasketId}; readers will be stale until its next change",
                basket.BasketId);
        }
    }
}
