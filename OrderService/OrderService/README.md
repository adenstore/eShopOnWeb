# OrderService (microservice)

Order-delen af eShopOnWeb er trukket ud af monolitten og kører nu som sin egen service med sin egen database (`Microsoft.eShopOnWeb.OrderDb`).

## Hvordan det hænger sammen

```
Web (monolit)                         OrderService
  Checkout ──► OrderCheckoutService ──HTTP POST /api/orders──► gemmer ordre i OrderDb
  Mine ordrer ──► OrderApiClient ─────HTTP GET  /api/orders──►      │
                                                                     ▼
                                                    IOrderEventPublisher (OrderCreated)
                                                    → RabbitMQ exchange "order-events"
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

# Web kalder OrderService gennem ApiGateway via "OrderServiceBaseUrl" i src/Web/appsettings.json (default http://localhost:5278/),
# så ApiGateway skal også køre: dotnet run --project src/ApiGateway --launch-profile http
dotnet run --project src/Web
```

Med Docker: `docker compose up` starter også `eshoporderservice` på port 5300. Databasen oprettes automatisk ved opstart (EF Core migrations).

## RabbitMQ

Når en ordre er gemt, kalder endpointet `IOrderEventPublisher.PublishAsync(OrderCreatedIntegrationEvent)`. `RabbitMqOrderEventPublisher` sender beskeden til RabbitMQ:

- **Exchange:** `order-events` (fanout, durable). Navnet ligger i `OrderService.Contracts` (`OrderEventsExchange.Name`), så andre services kan bruge det.
- **Mønster:** publish/subscribe. OrderService kender ikke modtagerne; en service, der vil have ordre-events, laver sin egen kø og binder den til exchangen.
- **Besked:** JSON (camelCase) af `OrderCreatedIntegrationEvent`. `Type` = `OrderCreatedIntegrationEvent`, `MessageId` = `EventId`, og beskeden er `Persistent`.
- **Fejl:** Hvis RabbitMQ er nede, logges fejlen, men ordren er allerede gemt, og kaldet lykkes stadig. Eventet går så tabt; et outbox-mønster ville løse det.
- **Tests** bruger `LoggingOrderEventPublisher`, så de ikke kræver en kørende RabbitMQ.

Konfiguration ligger i sektionen `RabbitMQ` (`HostName`, `Port`, `UserName`, `Password`). Lokalt bruges `localhost`/`guest`, i Docker `rabbitmq`/`eshop`.

```bash
# Kør RabbitMQ alene, hvis du kører OrderService uden for Docker
docker run -d --name rabbitmq -p 5672:5672 -p 15672:15672 rabbitmq:4-management
```

Management UI: `http://localhost:15672` (guest/guest lokalt, eshop/eshop i docker compose). Under *Exchanges* → `order-events` kan man se beskederne komme ind, og man kan binde en test-kø til exchangen for at læse dem.
