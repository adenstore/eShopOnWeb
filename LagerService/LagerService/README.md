# LagerService

Selvstændig microservice der holder styr på lageret: for hver vare (CatalogItemId fra CatalogService) gemmes antallet på lager.

## Database: Redis

Lageret er bare "VareID → antal", så servicen bruger **Redis** (key-value database) i stedet for SQL Server.
Det er et eksempel på *database per service* / *polyglot persistence*: ingen andre services rører lagerets data.

- Alle varer ligger i én Redis-hash: nøgle `lager:stock`, felt = CatalogItemId, værdi = antal.
- Nedskrivning sker med et lille Lua-script, der kører atomisk i Redis. To ordrer på samme tid kan derfor ikke begge tage den sidste vare, og lageret kan ikke blive negativt.
- `lager:handled-events` er et Redis-set med de event-id'er, der allerede er behandlet (se nedenfor).

## API

| Metode | URL | Beskrivelse |
|---|---|---|
| GET | `/api/stock` | Hele lageret |
| GET | `/api/stock/{catalogItemId}` | Antal af én vare (404 hvis varen ikke findes) |
| PUT | `/api/stock/{catalogItemId}` `{ "quantity": 20 }` | Sæt antallet (ny vare eller optælling) |
| POST | `/api/stock/{catalogItemId}/increase` `{ "amount": 10 }` | Varer modtaget |
| POST | `/api/stock/{catalogItemId}/decrease` `{ "amount": 2 }` | Varer solgt (409 hvis der ikke er nok) |

Gennem API Gateway (Ocelot) ligger de samme ruter under `/api/stock`.

## Systemintegration: RabbitMQ

Når en ordre oprettes, publicerer OrderService et `OrderCreatedIntegrationEvent` på fanout-exchangen `order-events`.
LagerService har sin egen durable kø `lager-service.order-events`, som er bundet til exchangen (publish/subscribe),
og trækker hver ordrelinjes `Units` fra lageret for `CatalogItemId`.

- Køen er durable, så beskeder venter i køen, hvis LagerService er nede, og behandles når den starter igen.
- Beskeder ack'es manuelt først når lageret er opdateret. Fejler Redis, lægges beskeden tilbage i køen.
- RabbitMQ leverer "mindst én gang", så samme event kan komme to gange. Event-id'et gemmes, og et event der allerede er behandlet springes over (idempotent consumer).
- Er der ikke nok på lager, logges en advarsel. Ordren er allerede oprettet, så LagerService kan ikke afvise den.

Servicen kender kun `OrderService.Contracts` (event-klassen), ikke selve OrderService.

## Kør den

Med docker compose (kopier `.env.example` til `.env` først):

```bash
docker compose up eshoplagerservice
```

API: http://localhost:5500/swagger, Redis på port 6379.

Lokalt uden Redis og RabbitMQ: sæt `"UseOnlyInMemoryDatabase": true` og `"RabbitMQ": { "Enabled": false }` i appsettings.

## Tests

```bash
dotnet test LagerService/LagerService.Tests
```

Testene kører med et in-memory lager og uden RabbitMQ.
