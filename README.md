# eShopOnWeb: fra monolit til microservices

Semesterprojekt, hvor Microsofts monolitiske eShopOnWeb gradvist bliver omdannet til en microservice-arkitektur ved hjælp af **Strangler Pattern**. Monolitten kører hele tiden videre, mens dele af den flyttes ud i selvstændige services. Services taler sammen asynkront over **RabbitMQ**, og frontenden går gennem en **API Gateway** (Ocelot).

README'en er bygget op efter semesteropgavens kravspecifikation. Afsnittet [Kravoversigt](#kravoversigt) viser for hvert krav, hvordan man konkret kan se, at det er opfyldt, og hvor i repoet det ligger.

> Projektet bygger på [dotnet-architecture/eShopOnWeb](https://github.com/dotnet-architecture/eShopOnWeb) (ASP.NET Core 8). Den oprindelige vejledning til Azure (`azd`) er fjernet herfra, fordi den beskriver monolitten og ikke den nuværende arkitektur.

## Indhold

1. [Kravoversigt](#kravoversigt)
2. [Arkitektur](#arkitektur)
3. [Services og afgrænsning](#services-og-afgrænsning)
4. [Strangler Pattern](#strangler-pattern)
5. [Lagerstyring (LagerService)](#lagerstyring-lagerservice)
6. [API Gateway](#api-gateway)
7. [Asynkron kommunikation med RabbitMQ](#asynkron-kommunikation-med-rabbitmq)
8. [Datahåndtering og dataseparation](#datahåndtering-og-dataseparation)
9. [Fejlhåndtering og overvågning](#fejlhåndtering-og-overvågning)
10. [CI/CD og containerisering](#cicd-og-containerisering)
11. [API-oversigt](#api-oversigt)
12. [Kom i gang](#kom-i-gang)
13. [Tests](#tests)
14. [Arkitekturbeslutninger](#arkitekturbeslutninger)
15. [Kendte mangler og videre arbejde](#kendte-mangler-og-videre-arbejde)

---

## Kravoversigt

Status: ✅ opfyldt · 🟡 delvist opfyldt · ❌ ikke opfyldt endnu

| # | Krav | Status | Sådan ser man, at det er opfyldt |
|---|---|---|---|
| 1 | Identificér og afgræns services (min. 3) | ✅ | Fire services: `CatalogService/`, `BasketService/`, `OrderService/` og `LagerService/`, hver med sin egen database. Begrundelsen for grænserne står i [Services og afgrænsning](#services-og-afgrænsning) og i hver services README. |
| 2 | Strangler Pattern | ✅ | Ordre, katalog og kurv er flyttet ud én ad gangen, mens Web blev ved med at virke. Migrationerne `MoveOrdersToOrderService` og `MoveCatalogToCatalogService` i `src/Infrastructure/Data/Migrations` fjerner tabellerne fra monolitten. Gatewayens catch-all-rute sender alt, der ikke er flyttet endnu, til monolittens `PublicApi`. Se [Strangler Pattern](#strangler-pattern). |
| 3 | Lagerstyringssystem som selvstændig microservice | ✅ | `LagerService/` har eget API (`/api/stock`), egen database (Redis) og trækker varer fra lageret, når den modtager `OrderCreated` fra RabbitMQ. Se [Lagerstyring](#lagerstyring-lagerservice). |
| 4 | API Gateway med autentificering, load balancing og rate limiting | 🟡 | `src/ApiGateway` (Ocelot) er det samlede indgangspunkt (`ocelot.json`). Autentificering sker i dag i services (JWT i CatalogService), ikke i gatewayen. Load balancing og rate limiting er ikke slået til endnu. Se [API Gateway](#api-gateway). |
| 5 | Asynkron beskedbaseret kommunikation | ✅ | RabbitMQ i `docker-compose.yml`. Exchanges `order-events`, `catalog-events`, `Basket` og `basket-events`. Integrationstests mod en rigtig RabbitMQ (Testcontainers) viser, at `OrderCreated` bliver publiceret og forbrugt. Se [RabbitMQ](#asynkron-kommunikation-med-rabbitmq). |
| 6 | Egen database pr. microservice | ✅ | `OrderDb`, `CatalogServiceDb`, `BasketDb` (SQL Server) og Redis for lageret. Ingen service læser en andens database; data deles via events, API-kald eller snapshots. Se [Datahåndtering](#datahåndtering-og-dataseparation). |
| 7 | Fejlhåndtering og overvågning | 🟡 | `/health` på alle services, retry med backoff mod RabbitMQ, `EnableRetryOnFailure` mod SQL Server, dead-letter-køer, idempotente consumers og struktureret logging. Circuit Breaker og centraliseret overvågning (ELK eller Prometheus/Grafana) mangler. Se [Fejlhåndtering](#fejlhåndtering-og-overvågning). |
| 8 | CI/CD pipeline og containerisering | 🟡 | `.github/workflows/dotnetcore.yml` bygger og tester hele løsningen ved hvert push og pull request. Alle services har en `Dockerfile` og startes med `docker compose`. Automatisk deployment (CD) mangler. Se [CI/CD](#cicd-og-containerisering). |
| 9 | Dokumentation af arkitekturbeslutninger, API'er og microservices | ✅ | Denne README, plus en README for hver service (`OrderService/OrderService/README.md`, `CatalogService/README.md`, `BasketService/README.md`, `LagerService/LagerService/README.md`, `src/ApiGateway/README.md`). Swagger på hver HTTP-service. |

---

## Arkitektur

```
                         Browser
                            │
            ┌───────────────┴────────────────┐
            ▼                                ▼
   Web (MVC, monolit)                 BlazorAdmin (WASM)
   identity, UI, checkout                    │
            │  REST                          │ REST
            └───────────────┬────────────────┘
                            ▼
                    ApiGateway (Ocelot)
     ┌──────────┬──────────┼──────────────┬───────────────┐
     │ HTTP     │ HTTP     │ HTTP         │ HTTP          │ AMQP (kurv)
     ▼          ▼          ▼              ▼               ▼
CatalogService OrderService LagerService PublicApi    exchange "Basket"
     │          │          ▲          (rest af monolit)   │
CatalogServiceDb OrderDb   │ Redis                         ▼
     │          │          │                         BasketService ── BasketDb
     │          ▼          │                               │
     │   exchange "order-events" (fanout) ─────────────────┤ (tømmer kurven)
     │                     └── LagerService (trækker fra lager)
     ▼                                                     ▼
exchange "catalog-events" (fanout)            exchange "basket-events" (fanout)
                                                → gatewayens kurv-projektion i Redis
```

| Komponent | Mappe | Rolle | Port (compose) |
|---|---|---|---|
| Web | `src/Web` | Monolittens MVC-frontend. Ejer stadig login/identity og checkout-flowet. | 5106 |
| PublicApi | `src/PublicApi` | Monolittens gamle API (login-token til BlazorAdmin). | 5200 |
| BlazorAdmin | `src/BlazorAdmin` | Admin-UI til kataloget, kører i browseren. | (serveres af Web) |
| ApiGateway | `src/ApiGateway` | Samlet indgangspunkt. Proxyer HTTP og omsætter kurv-kald til RabbitMQ. | 5278 |
| CatalogService | `CatalogService/` | Varer, mærker og typer. | 5400 |
| BasketService | `BasketService/` | Indkøbskurve. Kun RabbitMQ, ingen REST (kun `/health`). | 5198 |
| OrderService | `OrderService/` | Ordrer. Publicerer `OrderCreated`. | 5300 |
| LagerService | `LagerService/` | Lagerbeholdning. | 5500 |
| SQL Server | (image) | Databaser for Web, Catalog, Basket og Order. | 1433 |
| RabbitMQ | (image `rabbitmq:4-management`) | Message broker. | 5672, UI 15672 |
| Redis | (image `redis:7`) | Lagerets database og gatewayens kurv-projektion. | 6379 |

---

## Services og afgrænsning

Snittene er lagt efter forretningsdomæner (bounded contexts), så hver service har ét ansvar og én ejer af sine data.

| Service | Ansvar | Hvorfor grænsen ligger her |
|---|---|---|
| **CatalogService** | Hvad sælges: varer, mærker, typer, priser og billedstier. | Stamdata med én ejer (administratoren). Andre services har kun brug for at *henvise* til en vare med `CatalogItemId` og holder selv en kopi af de felter, de skal bruge. |
| **BasketService** | Kundens kurv indtil checkout. | Kurven ændrer sig hele tiden og har meget mere trafik end ordrer. Den gemmer prisen på det tidspunkt, varen blev lagt i, som sin egen data. |
| **OrderService** | Afgivne ordrer og deres historik. | En ordre er et juridisk dokument, der ikke må ændre sig. Den tager derfor et snapshot af navn, billede og pris (`CatalogItemOrdered`), så den aldrig skal spørge kataloget igen. |
| **LagerService** | Hvor mange af hver vare der er på lager. | Lagertal ændrer sig ved hvert salg, mens kataloget sjældent ændrer sig. Kataloget siger *hvad*, lageret siger *hvor mange*. |
| Monolitten (Web) | Login/identity, UI og checkout-orkestrering. | Endnu ikke flyttet. Identity udsteder de JWT'er, som services validerer. |

Opgaven nævner betaling som eksempel. Vi har ikke lavet en betalingsservice, fordi eShopOnWeb ikke har betaling; i stedet er lagerstyring den fjerde service.

---

## Strangler Pattern

Funktionalitet er flyttet ud én del ad gangen, og monolitten har kørt hele vejen igennem. Fremgangsmåden har været den samme for hver del:

1. **Byg den nye service ved siden af monolitten**, med egen database og samme API-form, så kalderne ikke skal ændres.
2. **Flyt trafikken**: Web får en HTTP-klient (`OrderApiClient`, `CatalogApiClient`, `BasketApiClient` i `src/Infrastructure/Services` og `src/Web/Services`), der kalder den nye service gennem gatewayen i stedet for at bruge sin egen database. Adressen styres af `OrderServiceBaseUrl`, `CatalogServiceBaseUrl` og `BasketServiceBaseUrl` i `src/Web/appsettings*.json`.
3. **Fjern den gamle kode og tabellerne fra monolitten**, når intet bruger dem mere. Det sker med en EF Core-migration.

| Rækkefølge | Del | Spor i repoet |
|---|---|---|
| 1 | Ordrer → OrderService | Migration `20261004113825_MoveOrdersToOrderService` fjerner `Orders`/`OrderItems` fra monolitten. |
| 2 | Katalog → CatalogService | Migration `MoveCatalogToCatalogService`; katalogkoden er slettet fra `ApplicationCore`, `Infrastructure` og `PublicApi` (se `CatalogService/README.md`). |
| 3 | Kurv → BasketService | Kurven er "cut over" til gatewayen, som sender kommandoer over RabbitMQ (PR #28–#32). |
| – | Lager → LagerService | Ny funktionalitet, der ikke fandtes i monolitten. |

**Strangler-facaden** er API Gatewayen. Ruter til flyttede dele går til de nye services, og den sidste rute i `ocelot.json` (`/api/{everything}`, prioritet 0) sender alt andet videre til monolittens `PublicApi`. Når endnu en del flyttes, tilføjes en mere specifik rute, og den gamle kode kan slettes uden at kalderne mærker det.

---

## Lagerstyring (LagerService)

LagerService holder styr på, hvor mange af hver vare (`CatalogItemId`) der er på lager.

- **Database:** Redis-hashen `lager:stock` (felt = `CatalogItemId`, værdi = antal). Nedskrivning sker med et Lua-script, der kører atomisk i Redis, så to samtidige ordrer ikke kan tage den sidste vare, og lageret aldrig bliver negativt.
- **Modtager opdateringer fra ordreafdelingen:** OrderService publicerer `OrderCreatedIntegrationEvent` på fanout-exchangen `order-events`. LagerService har sin egen durable kø `lager-service.order-events` og trækker hver ordrelinjes `Units` fra lageret.
- **Robust mod fejl:** Beskeder ack'es først, når lageret er opdateret. Fejler Redis, lægges beskeden tilbage i køen. Event-id'er gemmes i `lager:handled-events`, så et event, der kommer to gange, kun tæller én gang (idempotent consumer).
- **Løs kobling:** LagerService refererer kun til `OrderService.Contracts` (event-klassen), ikke til OrderService selv. OrderService ved ikke, at LagerService findes.
- **API:** `GET /api/stock`, `GET/PUT /api/stock/{id}`, `POST /api/stock/{id}/increase` og `/decrease` (409 ved for lidt på lager). Tilgængeligt via gatewayen.

**Begrænsning:** Reservation ved checkout er ikke lavet endnu. Lageret trækkes ned, *efter* ordren er oprettet; er der for lidt, logges en advarsel. Se [Kendte mangler](#kendte-mangler-og-videre-arbejde).

Detaljer: [`LagerService/LagerService/README.md`](LagerService/LagerService/README.md)

---

## API Gateway

`src/ApiGateway` er bygget på **Ocelot** og er det eneste sted, frontenden (Web og BlazorAdmin) kalder.

- **Routing:** `ocelot.json` (Docker-hostnavne) og `ocelot.Development.json` (localhost-porte). Katalog-, ordre- og lagerruter proxyes til de respektive services, og resten går til `PublicApi`.
- **Kurv over RabbitMQ:** BasketService har ingen REST. Gatewayen tager imod REST-kaldet og publicerer en kommando på exchangen `Basket` (svarer `202 Accepted`). Læsninger svares fra en projektion i Redis, fodret af `basket-events`, og ellers via request/reply over RabbitMQ. Se [`src/ApiGateway/README.md`](src/ApiGateway/README.md).

| Del af kravet | Status |
|---|---|
| Samlet indgangspunkt og routing | ✅ |
| Autentificering | 🟡 JWT'er udstedes af monolitten og valideres i CatalogService. Gatewayen validerer dem ikke selv endnu. |
| Load balancing | ❌ Hver rute har én downstream-host. |
| Rate limiting | ❌ Ikke slået til. |

Ocelot understøtter de manglende dele direkte i `ocelot.json` pr. rute: `AuthenticationOptions` (JWT bearer), `LoadBalancerOptions` (fx `RoundRobin` over flere `DownstreamHostAndPorts`) og `RateLimitOptions`.

---

## Asynkron kommunikation med RabbitMQ

Services kender ikke hinanden. De publicerer events på en exchange, og den, der vil have dem, laver sin egen kø og binder den (publish/subscribe). Alle beskeder er JSON (camelCase), persistente, og exchange-navnene ligger i hver services `*.Contracts`-projekt.

| Exchange | Type | Publiceres af | Besked | Forbruges af (kø) |
|---|---|---|---|---|
| `order-events` | fanout | OrderService | `OrderCreatedIntegrationEvent` | LagerService (`lager-service.order-events`), BasketService (`basket-order-events`) |
| `catalog-events` | fanout | CatalogService | `CatalogItemCreated/Updated/DeletedIntegrationEvent` (fuld tilstand) | Ingen i dag; klar til fx lager eller søgning |
| `Basket` | topic | ApiGateway | Kommandoer `Basket.addItem`, `Basket.setQuantities`, `Basket.transfer` og forespørgsler `Basket.getByBuyer`, `Basket.getById` | BasketService (`basket-service`) |
| `basket-events` | fanout | BasketService | `BasketChangedIntegrationEvent` (fuld tilstand) | ApiGateway (kurv-projektion) |
| `Basket.invalid` / `Basket.unroutable` | fanout | (dead-letter / alternate exchange) | Afviste eller fejl-routede beskeder | Køerne `invalid` og `unroutable-messages` |

**Købsflowet:**

1. Kunden lægger varer i kurven: Web → Gateway → `Basket.addItem` → BasketService gemmer → `basket-events` → gatewayens projektion opdateres.
2. Checkout: Web henter kurven (`Basket.getById`, altid frisk), tager et snapshot af varerne fra CatalogService og kalder `POST /api/orders` på OrderService gennem gatewayen.
3. OrderService gemmer ordren i `OrderDb` og publicerer `OrderCreated` på `order-events`.
4. LagerService trækker varerne fra lageret, og BasketService tømmer kurven, uafhængigt af hinanden.

At kurven tømmes på `OrderCreated` erstatter monolittens gamle `DeleteBasket`-kald, så man ikke kan ende med en oprettet ordre og en fuld kurv, hvis noget går ned midt i checkout.

RabbitMQ Management UI: <http://localhost:15672> (bruger og password fra `.env`).

---

## Datahåndtering og dataseparation

| Service | Database | Teknologi |
|---|---|---|
| OrderService | `Microsoft.eShopOnWeb.OrderDb` | SQL Server, EF Core |
| CatalogService | `Microsoft.eShopOnWeb.CatalogServiceDb` | SQL Server, EF Core |
| BasketService | `Microsoft.eShopOnWeb.BasketDb` | SQL Server, EF Core |
| LagerService | Redis, nøgler `lager:*` | Redis (key-value) |
| ApiGateway | Redis, nøgler `basket:*` (kun en læse-kopi) | Redis |
| Monolitten | `CatalogDb` (resterende tabeller) og `Identity` | SQL Server |

- Hver service ejer sine tabeller, og ingen anden service læser dem direkte. Services, der har brug for andres data, får dem via **events** (lager og kurv reagerer på `OrderCreated`), **API-kald** (Web henter varedata fra CatalogService) eller **snapshots** (ordren gemmer navn og pris fra kataloget).
- Databaserne kører på samme SQL Server-container for at spare ressourcer lokalt, men er separate databaser med hver sin connection string. De kan flyttes til hver sin server uden kodeændringer.
- LagerService bruger Redis i stedet for SQL Server (polyglot persistence), fordi lageret kun er "vare-id → antal" og skal kunne skrives ned atomisk.
- Databaser oprettes og migreres automatisk ved opstart (EF Core migrations).

---

## Fejlhåndtering og overvågning

**Det er lavet:**

- **Health checks:** `GET /health` på OrderService, CatalogService, BasketService og LagerService.
- **Retry:** SQL Server-forbindelser bruger `EnableRetryOnFailure`. Consumers prøver igen med backoff, hvis RabbitMQ ikke er klar endnu (fx ved opstart i compose), i stedet for at lukke servicen ned. Redis-forbindelser bruger `AbortOnConnectFail = false`.
- **Manuel ack, dead-lettering og idempotens:** LagerService lægger en besked tilbage i køen, hvis den ikke kunne behandles, og springer events over, den allerede har set. BasketService sender ugyldige beskeder til `invalid`-køen og fejl-routede til `unroutable-messages`, så de kan undersøges i stedet for at forsvinde.
- **Publisher confirms** i gatewayen: en kommando giver kun `202`, når brokeren har bekræftet den, ellers `503`.
- **Graceful degradation:** Er Redis nede, svarer gatewayen kurv-læsninger via RabbitMQ i stedet for at fejle. Er RabbitMQ nede, når en ordre gemmes, lykkes ordren stadig, og fejlen logges.
- **Logging** via `ILogger` i alle services.

**Det mangler:** Circuit Breaker (fx Polly eller Ocelots `QoSOptions`), et outbox-mønster, så events ikke kan gå tabt, og centraliseret logging og metrikker (ELK eller Prometheus/Grafana).

---

## CI/CD og containerisering

- **CI:** `.github/workflows/dotnetcore.yml` kører ved hvert push, pull request og manuelt. Den bygger `eShopOnWeb.sln` i Release og kører alle tests, inkl. integrationstestene mod RabbitMQ og Redis i containere (Testcontainers). En pull request merges ikke, før den er grøn.
- **Containerisering:** Alle services har en `Dockerfile`, og `docker-compose.yml` + `docker-compose.override.yml` starter hele systemet med SQL Server, RabbitMQ og Redis. Hemmeligheder ligger i `.env` (ikke i Git); `.env.example` er skabelonen.
- **Mangler (CD):** Pipelinen bygger ikke Docker-images og udruller ikke. Næste skridt er `docker build`/`push` til et registry, tagget med commit-SHA, og udrulning ved merge til `master`.

---

## API-oversigt

Alle HTTP-services har Swagger UI på `/swagger`. Gennem gatewayen ligger ruterne på `http://localhost:5278`.

**OrderService** (`http://localhost:5300`)

| Metode | Rute | Beskrivelse |
|---|---|---|
| POST | `/api/orders` | Opret ordre. `201` med `{ orderId }`, `400` ved valideringsfejl. Publicerer `OrderCreated`. |
| GET | `/api/orders?buyerId=…` | Alle ordrer for en køber. |
| GET | `/api/orders/{id}?buyerId=…` | Én ordre. `404`, hvis den ikke findes eller tilhører en anden køber. |

**CatalogService** (`http://localhost:5400`)

| Metode | Rute | Adgang |
|---|---|---|
| GET | `/api/catalog-items?pageIndex=&pageSize=&catalogBrandId=&catalogTypeId=` | Alle |
| GET | `/api/catalog-items/{id}` | Alle |
| POST / PUT | `/api/catalog-items` | Rollen `Administrators` (JWT) |
| DELETE | `/api/catalog-items/{id}` | Rollen `Administrators` (JWT) |
| GET | `/api/catalog-brands`, `/api/catalog-types` | Alle |

**LagerService** (`http://localhost:5500`)

| Metode | Rute | Beskrivelse |
|---|---|---|
| GET | `/api/stock` | Hele lageret |
| GET | `/api/stock/{catalogItemId}` | Antal af én vare |
| PUT | `/api/stock/{catalogItemId}` `{ "quantity": 20 }` | Sæt antal |
| POST | `/api/stock/{catalogItemId}/increase` `{ "amount": 10 }` | Varer modtaget |
| POST | `/api/stock/{catalogItemId}/decrease` `{ "amount": 2 }` | Varer solgt (`409` ved for lidt) |

**Kurv via ApiGateway** (`http://localhost:5278`)

| Metode | Rute | Bliver til |
|---|---|---|
| POST | `/api/baskets/{buyerId}/items` | `Basket.addItem` (202) |
| PUT | `/api/baskets/{basketId}/items` | `Basket.setQuantities` (202) |
| POST | `/api/baskets/transfer` | `Basket.transfer` (202) |
| GET | `/api/baskets/{buyerId}` og `/items/count` | Projektion eller `Basket.getByBuyer` |
| GET | `/api/baskets/{basketId}` | `Basket.getById` (altid frisk) |

---

## Kom i gang

**Krav:** .NET 8 SDK og Docker Desktop.

### Hele systemet med Docker

```bash
# 1. Lav .env ud fra skabelonen og udfyld passwords
cp .env.example .env        # Windows: copy .env.example .env

# 2. Byg og start alt
docker compose build
docker compose up
```

`SA_PASSWORD` skal indtil videre matche det password, der står i `appsettings.Docker.json` for Web og PublicApi (se kommentaren i `.env.example`).

| Hvad | Adresse |
|---|---|
| Butikken (Web) | <http://localhost:5106> (login: `demouser@microsoft.com` / `Pass@word1`) |
| Admin (BlazorAdmin) | <http://localhost:5106/admin> (login: `admin@microsoft.com` / `Pass@word1`) |
| API Gateway | <http://localhost:5278> |
| OrderService Swagger | <http://localhost:5300/swagger> |
| CatalogService Swagger | <http://localhost:5400/swagger> |
| LagerService Swagger | <http://localhost:5500/swagger> |
| RabbitMQ Management | <http://localhost:15672> |

**Prøv integrationen:** Læg varer i kurven, gå til checkout, og se i RabbitMQ Management under *Exchanges → order-events*, at `OrderCreated` bliver sendt. `GET http://localhost:5500/api/stock` viser bagefter, at lageret er gået ned, og kurven er tom.

### En enkelt service lokalt

```bash
# Infrastruktur i Docker
docker compose up -d sqlserver rabbitmq redis

# Fx OrderService uden SQL Server
UseOnlyInMemoryDatabase=true dotnet run --project OrderService/OrderService --urls http://localhost:5300

# Gatewayen (bruger ocelot.Development.json med localhost-porte)
dotnet run --project src/ApiGateway --launch-profile http
```

Hver service har sin egen vejledning i sin README.

---

## Tests

```bash
dotnet test eShopOnWeb.sln      # alt, som CI gør (kræver Docker til integrationstestene)
```

| Projekt | Type | Hvad testes |
|---|---|---|
| `OrderService/OrderService.Tests` | Unit, endpoint, integration | Ordrens totaler, alle endpoints via `WebApplicationFactory`, og at `OrderCreated` faktisk lander på RabbitMQ 4 (Testcontainers). |
| `LagerService/LagerService.Tests` | Unit, endpoint, integration | Lager-endpoints, `OrderCreatedHandler`, idempotens, og at et `OrderCreated` fra en rigtig RabbitMQ trækker lageret ned. |
| `CatalogService/CatalogService.Tests` | Unit, endpoint | Domæneregler, specifikationer, endpoints inkl. `401`/`403`/`409` og at det rigtige event sendes. |
| `BasketService/BasketService.Tests` | Unit | Domæne, services, beskedvalidering og wire-format. |
| `BasketService/BasketService.IntegrationTests` | Integration | Kommandoer/forespørgsler over RabbitMQ og at `OrderCreated` tømmer kurven. |
| `tests/ApiGatewayTests` | Endpoint | Kurv-ruter (202/400/503), projektionens regler. |
| `tests/ApiGatewayIntegrationTests` | Integration | Redis-projektionen mod en rigtig Redis. |
| `tests/UnitTests`, `FunctionalTests`, `PublicApiIntegrationTests` | Fra originalen | Monolittens resterende dele. |

---

## Arkitekturbeslutninger

| Beslutning | Begrundelse | Alternativ fravalgt |
|---|---|---|
| Strangler Pattern med gatewayen som facade | Monolitten kan køre hele tiden; én del flyttes ad gangen og kan rulles tilbage. | Big-bang-omskrivning. |
| Ocelot som API Gateway | .NET-native og konfigureres i JSON; understøtter auth, load balancing og rate limiting pr. rute. | YARP, nginx. |
| RabbitMQ som broker | Simpel at køre i Docker, understøtter fanout, topic, dead-lettering og request/reply. | Kafka (tungere, mest relevant ved event-streaming), Azure Service Bus (kræver Azure). |
| Publish/subscribe med fanout til events | Producenten kender ikke modtagerne, så nye services (fx lager) kan lytte med uden at ændre OrderService. | Direkte HTTP-kald mellem services. |
| Events bærer fuld tilstand | Idempotente og selvhelende; en tabt eller dobbelt besked gør ingen skade. | Delta-events, som kræver rækkefølge og exactly-once. |
| Ordre tager snapshot af varedata | En ordre må ikke ændre sig, når kataloget gør; OrderService skal ikke kalde CatalogService. | Slå varen op ved visning. |
| Kurven tømmes på `OrderCreated` | Ingen fordelt transaktion; ingen ordre med fuld kurv efter et nedbrud. | Web kalder `DeleteBasket` efter ordren. |
| BasketService kun over RabbitMQ | Opgavens krav om asynkron kommunikation indad fra gatewayen. Læsninger accelereres af en projektion. | REST på BasketService. |
| Redis til lageret | Lageret er key-value, og atomisk nedskrivning er simpelt med Lua. | SQL Server med låse. |
| Monorepo og én `docker-compose.yml` | Nemt at dele kontrakter og starte hele systemet. | Ét repo pr. service. |
| Hemmeligheder i `.env` | Passwords holdes ude af Git. | Passwords i `appsettings`. |

---

## Kendte mangler og videre arbejde

- **API Gateway:** JWT-validering, load balancing og rate limiting i `ocelot.json`.
- **Lager:** Reservation af varer ved checkout, før ordren oprettes, og evt. at afvise en ordre, når der ikke er nok på lager.
- **Overvågning:** Circuit Breaker og centraliseret logging/metrikker (Prometheus/Grafana eller ELK).
- **Pålidelige events:** Outbox-mønster i OrderService og CatalogService, så events ikke går tabt, hvis RabbitMQ er nede i det øjeblik, ændringen gemmes.
- **CD:** Byg og push Docker-images og udrul automatisk ved merge til `master`.
- **Identity:** Login ligger stadig i monolitten og er næste kandidat til at blive trukket ud.
- **Konfiguration:** Web og PublicApi skal læse SQL-password fra `.env` som de øvrige services.
