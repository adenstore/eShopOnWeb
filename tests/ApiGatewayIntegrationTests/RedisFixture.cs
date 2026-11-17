using Microsoft.eShopWeb.ApiGateway.Projections;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace Microsoft.eShopWeb.ApiGateway.IntegrationTests;

/// <summary>
/// Starts a real Redis in a container and hands out projections backed by it.
/// <para>
/// The real thing rather than a fake, because what is under test is the part of the projection that
/// does not run in .NET at all: the compare-and-write rules are Lua, evaluated inside Redis so that
/// each of them is one uninterruptible step. A fake would only prove that the C# around them was
/// called.
/// </para>
/// <para>
/// Requires a container runtime (Docker or Podman) to be running, like
/// <c>BasketService.IntegrationTests</c>.
/// </para>
/// </summary>
public class RedisFixture : IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7")
        .Build();

    private ConnectionMultiplexer _connection = null!;

    public async Task InitializeAsync()
    {
        await _redis.StartAsync();
        _connection = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
        await _redis.DisposeAsync();
    }

    /// <summary>
    /// A projection on the shared Redis. Handed out per call rather than kept, so a test can prove
    /// the state really is in Redis by reading it back through a second one.
    /// </summary>
    public IBasketProjection Projection() =>
        new RedisBasketProjection(_connection, NullLogger<RedisBasketProjection>.Instance);

    /// <summary>For asserting on the keys themselves — the expiries, mostly.</summary>
    public IDatabase Database => _connection.GetDatabase();
}
