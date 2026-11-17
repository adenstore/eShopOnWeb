using System.Text.Json;
using Microsoft.eShopWeb.BasketService.Contracts;
using RabbitMQ.Client;

namespace Microsoft.eShopWeb.ApiGateway.Messaging;

public interface IBasketCommandPublisher
{
    /// <summary>
    /// Publishes a one-way basket command. Throws if the broker did not accept it, so the caller
    /// can answer the HTTP request honestly instead of reporting an accepted command that is gone.
    /// </summary>
    Task PublishAsync(string routingKey, object command, CancellationToken cancellationToken = default);
}

/// <summary>
/// Publishes basket commands to the "Basket" topic exchange. This is the gateway's half of the
/// required architecture: REST comes in at the edge, and everything behind the edge is messages.
/// <para>
/// It does not declare the exchange. BasketService owns that topology — including the
/// <c>alternate-exchange</c> argument on "Basket" — and declaring the same exchange with different
/// arguments is a channel-killing <c>406 PRECONDITION_FAILED</c>, so copying the arguments here
/// would just be a second place to keep them in step. The consequence is deliberate: if
/// BasketService has never been started, there is no exchange, the publish fails, and the caller
/// gets 503 rather than a 202 for a command nothing will ever read.
/// </para>
/// </summary>
public sealed class RabbitMqBasketCommandPublisher : IBasketCommandPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Publisher confirmations are off by default in RabbitMQ.Client, which makes
    /// <c>BasicPublishAsync</c> fire-and-forget: a missing exchange or a broker refusing writes
    /// comes back as an asynchronous channel error, long after the call returned "successfully".
    /// With confirmations the publish waits for the broker's ack and throws a
    /// <see cref="RabbitMQ.Client.Exceptions.PublishException"/> otherwise — which is the only way
    /// a 202 from this gateway can mean anything.
    /// </summary>
    private static readonly CreateChannelOptions ChannelOptions = new(
        publisherConfirmationsEnabled: true,
        publisherConfirmationTrackingEnabled: true);

    private readonly IRabbitMqConnection _connection;
    private readonly ILogger<RabbitMqBasketCommandPublisher> _logger;

    public RabbitMqBasketCommandPublisher(IRabbitMqConnection connection,
        ILogger<RabbitMqBasketCommandPublisher> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    public async Task PublishAsync(string routingKey, object command,
        CancellationToken cancellationToken = default)
    {
        var connection = await _connection.GetAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(ChannelOptions, cancellationToken);

        var properties = new BasicProperties
        {
            ContentType = "application/json",
            // The consumer dispatches on the routing key; Type is here for whoever is reading the
            // message in the management UI or on the "invalid" queue.
            Type = command.GetType().Name,
            // Persistent, because a command that is lost when the broker restarts is a basket
            // change the shopper already saw accepted.
            Persistent = true
        };

        await channel.BasicPublishAsync(BasketMessaging.CommandExchange, routingKey, mandatory: false,
            basicProperties: properties,
            body: JsonSerializer.SerializeToUtf8Bytes(command, JsonOptions),
            cancellationToken: cancellationToken);

        _logger.LogInformation("Published {RoutingKey} to exchange {Exchange}",
            routingKey, BasketMessaging.CommandExchange);
    }
}
