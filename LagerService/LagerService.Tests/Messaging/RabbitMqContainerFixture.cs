using Testcontainers.RabbitMq;
using Xunit;

namespace Microsoft.eShopWeb.LagerService.Tests.Messaging;

/// <summary>
/// Opgave 3A – Infrastruktur som kode.
/// Starter en ægte RabbitMQ i Docker, før testene kører, og fjerner den igen bagefter.
/// Testen ejer altså selv sin infrastruktur: ingen skal starte docker compose først.
/// Kræver at Docker Desktop kører på maskinen.
/// </summary>
/// <remarks>
/// IAsyncLifetime er xUnits måde at lave asynkron opsætning og oprydning på:
/// InitializeAsync kører før den første test, DisposeAsync efter den sidste.
/// </remarks>
public sealed class RabbitMqContainerFixture : IAsyncLifetime
{
    // RabbitMQ tillader kun brugeren "guest" fra localhost inde i containeren,
    // så vi opretter vores egen bruger, som testene kan logge ind med udefra.
    public const string UserName = "eshop";
    public const string Password = "eshop";

    // 1. Definér containeren (slide "Opgavekort 3A", trin 1).
    //    "3-management" er RabbitMQ med web-UI'et, samme image som i docker-compose.
    private readonly RabbitMqContainer _container = new RabbitMqBuilder()
        .WithImage("rabbitmq:3-management")
        .WithUsername(UserName)
        .WithPassword(Password)
        .Build();

    /// <summary>Værten containeren kan nås på (normalt localhost).</summary>
    public string HostName => _container.Hostname;

    /// <summary>
    /// Docker mapper AMQP-porten 5672 til en tilfældig ledig port på værten,
    /// så flere containere kan køre samtidig. Derfor må 5672 aldrig hardkodes i testene.
    /// </summary>
    public int Port => _container.GetMappedPublicPort(RabbitMqBuilder.RabbitMqPort);

    /// <summary>
    /// 3. Forbindelsesstrengen (amqp://bruger:kode@vært:port/) til klienter, der tager en URI.
    /// </summary>
    public string ConnectionString => _container.GetConnectionString();

    // 2. Start containeren før testene (slide trin 2). Testcontainers venter selv,
    //    til RabbitMQ er klar til at tage imod forbindelser, før den returnerer.
    public Task InitializeAsync() => _container.StartAsync();

    // Stopper og sletter containeren, når alle testene i samlingen er færdige.
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

/// <summary>
/// En xUnit "collection": alle testklasser med [Collection("RabbitMQ")] deler den SAMME
/// container. Uden den ville hver testklasse starte sin egen container (ca. 15 sekunder hver).
/// Tests i samme collection køres efter hinanden, ikke parallelt.
/// </summary>
[CollectionDefinition(Name)]
public class RabbitMqCollection : ICollectionFixture<RabbitMqContainerFixture>
{
    public const string Name = "RabbitMQ";
}
