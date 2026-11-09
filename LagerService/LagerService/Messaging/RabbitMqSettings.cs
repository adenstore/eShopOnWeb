namespace Microsoft.eShopWeb.LagerService.Messaging;

/// <summary>
/// Bound from the "RabbitMQ" section in appsettings.
/// </summary>
public class RabbitMqSettings
{
    public const string SectionName = "RabbitMQ";

    /// <summary>Set to false to run the service without a broker (the tests do this).</summary>
    public bool Enabled { get; set; } = true;
    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";

    /// <summary>LagerService's own queue, bound to OrderService's "order-events" exchange.</summary>
    public string QueueName { get; set; } = "lager-service.order-events";
}
