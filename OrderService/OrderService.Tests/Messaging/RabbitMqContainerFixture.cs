using Testcontainers.RabbitMq;
using Xunit;

namespace Microsoft.eShopWeb.OrderService.Tests.Messaging;

/// <summary>
/// Opgave 3A: starts a real RabbitMQ in Docker before the tests and removes it afterwards.
/// Shared by every test class in the "RabbitMQ" collection, so the container only starts once.
/// Docker must be running on the machine.
/// </summary>
public sealed class RabbitMqContainerFixture : IAsyncLifetime
{
    public const string UserName = "eshop";
    public const string Password = "eshop";

    private readonly RabbitMqContainer _container = new RabbitMqBuilder()
        .WithImage("rabbitmq:3-management")
        .WithUsername(UserName)
        .WithPassword(Password)
        .Build();

    /// <summary>Host the container can be reached on (usually localhost).</summary>
    public string HostName => _container.Hostname;

    /// <summary>Docker maps the AMQP port 5672 to a random free port on the host, so never hard-code 5672.</summary>
    public int Port => _container.GetMappedPublicPort(RabbitMqBuilder.RabbitMqPort);

    /// <summary>amqp://user:password@host:port/ for clients that take a URI.</summary>
    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public class RabbitMqCollection : ICollectionFixture<RabbitMqContainerFixture>
{
    public const string Name = "RabbitMQ";
}
