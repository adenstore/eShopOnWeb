namespace Microsoft.eShopWeb.ApiGateway.Messaging;

/// <summary>
/// Bound from the "RabbitMQ" section in appsettings. Same shape as the settings in BasketService,
/// CatalogService, OrderService and LagerService, so one compose environment block configures them
/// all the same way.
/// </summary>
public class RabbitMqSettings
{
    public const string SectionName = "RabbitMQ";

    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
}
