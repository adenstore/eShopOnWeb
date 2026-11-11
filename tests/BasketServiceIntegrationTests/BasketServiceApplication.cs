using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.BasketService.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Testcontainers.RabbitMq;
using Xunit;

namespace Microsoft.eShopWeb.BasketServiceIntegrationTests;

/// <summary>
/// Starts a real RabbitMQ in a container and runs BasketService against it in memory.
/// <para>
/// Only the database is faked. The broker is the real thing, because the thing under test *is* the
/// messaging: routing, dead-lettering and the reply protocol are broker behaviour, and a mocked bus
/// would only prove that the mock was called — not that the message went anywhere.
/// </para>
/// <para>
/// Requires a container runtime (Docker or Podman) to be running.
/// </para>
/// </summary>
public class BasketServiceApplication : WebApplicationFactory<Program>, IAsyncLifetime
{
    // EF Core shares in-memory databases by name across the whole test run, so a unique name keeps
    // one run's baskets out of another's.
    private readonly string _databaseName = $"BasketTests-{Guid.NewGuid()}";

    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder()
        .WithImage("rabbitmq:4-management")
        .Build();

    private Uri _brokerUri = null!;

    public string RabbitMqHost => _brokerUri.Host;

    /// <summary>The host port the container's 5672 was mapped to — different on every run.</summary>
    public int RabbitMqPort => _brokerUri.Port;

    public string RabbitMqUser => _brokerUri.UserInfo.Split(':')[0];

    public string RabbitMqPassword => _brokerUri.UserInfo.Split(':')[1];

    async Task IAsyncLifetime.InitializeAsync()
    {
        await _rabbitMq.StartAsync();

        // amqp://user:pass@host:port — read rather than hardcoded, so this keeps working if the
        // Testcontainers defaults ever change.
        _brokerUri = new Uri(_rabbitMq.GetConnectionString());

        // Touching the client forces the host to build and the consumers to start, so a test does
        // not have to remember to do it before publishing its first message.
        _ = CreateClient();
    }

    // WebApplicationFactory already has a public ValueTask DisposeAsync(), so IAsyncLifetime's
    // Task-returning one has to be implemented explicitly rather than as a second public method.
    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _rabbitMq.DisposeAsync();
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // The container picks a random host port, so the service can only be told where its broker
        // is after the container is up — which is exactly what Testcontainers buys over a fixed
        // docker-compose port, and why this cannot live in appsettings.
        builder.ConfigureHostConfiguration(config =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RabbitMQ:HostName"] = RabbitMqHost,
                ["RabbitMQ:Port"] = RabbitMqPort.ToString(),
                ["RabbitMQ:UserName"] = RabbitMqUser,
                ["RabbitMQ:Password"] = RabbitMqPassword
            }));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<BasketDbContext>>();
            services.AddDbContext<BasketDbContext>(options => options.UseInMemoryDatabase(_databaseName));
        });

        return base.CreateHost(builder);
    }
}

/// <summary>
/// One container and one service host shared by every test class below, rather than one per class.
/// It also makes the classes run one after another, which matters because some of them assert on
/// queue depths that another class's messages would otherwise move.
/// </summary>
[CollectionDefinition(Name)]
public class BasketServiceCollection : ICollectionFixture<BasketServiceApplication>
{
    public const string Name = "BasketService";
}
