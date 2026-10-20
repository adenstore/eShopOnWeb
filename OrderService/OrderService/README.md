# OrderService (microservice)

Order-delen af eShopOnWeb er trukket ud af monolitten og kører nu som sin egen service med sin egen database (`Microsoft.eShopOnWeb.OrderDb`).

## Hvordan det hænger sammen

```
Web (monolit)                         OrderService
  Checkout ──► OrderCheckoutService ──HTTP POST /api/orders──► gemmer ordre i OrderDb
  Mine ordrer ──► OrderApiClient ─────HTTP GET  /api/orders──►      │
                                                                     ▼
                                                    IOrderEventPublisher (OrderCreated)
                                                    → senere: RabbitMQ
```

- **Web** ejer stadig kurv og katalog. Ved checkout tager den et "snapshot" af varerne (navn, billede, pris) og sender dem til OrderService, så OrderService aldrig skal spørge kataloget.
- **OrderService** ejer tabellerne `Orders` og `OrderItems`. Ingen andre må læse dem direkte.
- **OrderService.Contracts** indeholder de DTO'er og events, som begge sider deler (`CreateOrderRequest`, `OrderDto`, `OrderCreatedIntegrationEvent`).

## API

| Metode | Rute | Beskrivelse |
|---|---|---|
| POST | `/api/orders` | Opret ordre. Returnerer `201` med `{ orderId }`. |
| GET | `/api/orders?buyerId=...` | Alle ordrer for en køber. |
| GET | `/api/orders/{id}?buyerId=...` | Én ordre; `404` hvis den ikke findes eller tilhører en anden køber. |
| GET | `/health` | Health check. |

Swagger UI: `http://localhost:5300/swagger`

## Kør lokalt

```bash
# Uden SQL Server (in-memory database)
UseOnlyInMemoryDatabase=true dotnet run --project OrderService/OrderService --urls http://localhost:5300

# Web finder servicen via "OrderServiceBaseUrl" i src/Web/appsettings.json (default http://localhost:5300/)
dotnet run --project src/Web
```

Med Docker: `docker compose up` starter også `eshoporderservice` på port 5300. Databasen oprettes automatisk ved opstart (EF Core migrations).

## RabbitMQ (næste skridt)

Når en ordre er gemt, kalder endpointet `IOrderEventPublisher.PublishAsync(OrderCreatedIntegrationEvent)`. Lige nu logger `LoggingOrderEventPublisher` bare beskeden. For at bruge RabbitMQ laver man en `RabbitMqOrderEventPublisher : IOrderEventPublisher` og skifter registreringen i `Program.cs` – endpoints og tests skal ikke ændres.
