# CatalogService (microservice)

Katalog-delen af eShopOnWeb (varer, mærker og typer) er trukket ud af monolitten og kører som sin egen service med sin egen database (`Microsoft.eShopOnWeb.CatalogServiceDb`). Udtrækningen er lavet efter **strangler-mønsteret**: koden lå begge steder, mens trafikken blev flyttet over til den nye service, og da intet i monolitten længere brugte det gamle katalog, blev det slettet. Kataloget findes nu kun i CatalogService.

## Hvorfor grænsen ligger her

Kataloget er **stamdata**: hvilke varer findes, hvad hedder de, hvad koster de, og hvilket billede har de. Det er ét sammenhængende ansvar med én ejer (administratoren i BlazorAdmin), og de andre dele af systemet har kun brug for at *henvise* til en vare. Derfor er snittet lagt sådan:

| Hører til CatalogService | Hører **ikke** til CatalogService | Hvorfor |
|---|---|---|
| Varer (`CatalogItem`): navn, beskrivelse, pris, billede | Kurven og dens linjer | Kurven gemmer kun `CatalogItemId` + den pris, varen havde, da den blev lagt i. Prisen i kurven er kurvens egen data. |
| Mærker (`CatalogBrand`) og typer (`CatalogType`) | Ordrer | En ordre tager et snapshot af navn, billede og pris (`CatalogItemOrdered` i OrderService). En gammel ordre må ikke ændre sig, fordi en vare senere bliver omdøbt eller slettet. |
| Opret, ret og slet varer (admin) | Lagerbeholdning | Antal på lager ændrer sig ved hvert salg og ejes af LagerService. Kataloget siger *hvad* der sælges, lageret *hvor mange*. |
| Events, når en vare ændres | Brugere, login og roller | Identity bliver i monolitten. CatalogService validerer kun de JWT'er, monolitten udsteder. |
| | Selve billedfilerne | De ligger stadig i `src/Web/wwwroot/images/products` og serveres af Web. CatalogService gemmer kun stien. |

Mærker og typer ligger med i servicen, fordi de kun bruges til at filtrere og beskrive varer. De har ingen betydning uden for kataloget.

Reglen for andre services er: **brug `CatalogItemId` som reference, og hold selv en kopi af de felter, du har brug for** (via events), i stedet for at læse katalogets database.

## Projekter

| Projekt | Indhold |
|---|---|
| `CatalogService.Api` | Minimal-API endpoints, JWT-validering, CORS, Swagger, `/health`, opstart og seed. |
| `CatalogService.Domain` | Entiteter (`CatalogItem`, `CatalogBrand`, `CatalogType`), specifikationer og interfaces. Ingen afhængighed til EF Core. |
| `CatalogService.Infrastructure` | `CatalogContext` (EF Core, SQL Server), migrations, seed-data og RabbitMQ-publisheren. |
| `CatalogService.Contracts` | DTO'er og integration events, som andre projekter må referere til (Web gør det). |
| `CatalogService.Tests` | xUnit + FluentAssertions. Domæne, specifikationer og alle endpoints gennem `WebApplicationFactory`. |

Endpoints, specifikationer og entiteter er flyttet fra monolittens `PublicApi` og `ApplicationCore`, så de svarer til originalen. Det nye er opdelingen i lag, den egen database og RabbitMQ-events.

## Hvordan det hænger sammen

```
BlazorAdmin (browser) ──REST──► ApiGateway ──► CatalogService ──► CatalogServiceDb
Web: forside, kurv ────REST (ICatalogApiClient)──►     │
Web: checkout ─────────REST (ICatalogItemLookup)─►     │ CatalogItemCreated/Updated/Deleted
                                                       ▼
                                      RabbitMQ exchange "catalog-events" (fanout)
```

- **BlazorAdmin** (admin-siderne) kalder gatewayen, som sender `/api/catalog-items`, `/api/catalog-items/{id}`, `/api/catalog-brands` og `/api/catalog-types` videre til CatalogService.
- **Web's forside** (produktlisten med filtre) læser fra CatalogService via `ICatalogApiClient`. Svarene caches som før af `CachedCatalogViewModelService`.
- **Kurven** (`Web/Pages/Basket/Index.cshtml.cs` og `Web/Services/BasketViewModelService.cs`) henter pris, navn og billede fra CatalogService via `ICatalogApiClient.GetCatalogItemAsync`.
- **Checkout** (`ApplicationCore/Services/OrderCheckoutService.cs`) henter navn og billede til ordrens snapshot via `ICatalogItemLookup`. Interfacet ligger i ApplicationCore, fordi ApplicationCore ikke må referere til Web; implementeringen (`Web/Services/CatalogApiItemLookup.cs`) bruger `ICatalogApiClient`.

## Fjernet fra monolitten

Da kurv og checkout spurgte CatalogService, brugte intet i monolitten længere dens eget katalog, og det blev slettet:

| Hvad | Hvor det lå |
|---|---|
| Katalog-entiteter, specifikationer, `CatalogSettings`, `IUriComposer`/`UriComposer` | `src/ApplicationCore` |
| EF-konfigurationer og seed-data for kataloget | `src/Infrastructure/Data` |
| Katalog-endpoints og `MappingProfile` | `src/PublicApi` (gatewayen sender allerede katalog-ruterne til CatalogService) |
| Synkronisering af monolittens katalog-kopi (`CatalogEventsListener`, `CatalogEventSync`) og fallback til databasen (`CatalogViewModelService`) samt flagene `UseCatalogService` og `SyncCatalogFromEvents` | `src/Web` |
| Tabellerne `Catalog`, `CatalogBrands` og `CatalogTypes` | Monolittens database; fjernet af migrationen `MoveCatalogToCatalogService` (samme fremgangsmåde som `MoveOrdersToOrderService`) |

Tilbage i monolitten er kun:
- `Infrastructure/Data/CatalogContext.cs`, som trods navnet nu kun indeholder kurvens tabeller. Navnet er bevaret, så kurv-koden og de eksisterende migrations ikke skal ændres. Migrations køres ved opstart af `CatalogContextMigrator` (tidligere gjorde seed-klassen det).
- Den gamle Razor-admin-side `Web/Pages/Admin/EditCatalogItem`, som er kommenteret ud og ikke kan nås.

## API

| Metode | Rute | Adgang | Beskrivelse |
|---|---|---|---|
| GET | `/api/catalog-items?pageIndex=&pageSize=&catalogBrandId=&catalogTypeId=` | Alle | Side med varer, evt. filtreret. `pageSize=0` giver alle. |
| GET | `/api/catalog-items/{id}` | Alle | Én vare; `404` hvis den ikke findes. |
| POST | `/api/catalog-items` | Administrators | Opret vare. `409` hvis navnet findes. Får altid standardbilledet, fordi billed-upload er slået fra (som i originalen). |
| PUT | `/api/catalog-items` | Administrators | Ret navn, beskrivelse, pris, mærke og type. |
| DELETE | `/api/catalog-items/{id}` | Administrators | Slet vare. |
| GET | `/api/catalog-brands` | Alle | Alle mærker. |
| GET | `/api/catalog-types` | Alle | Alle typer. |
| GET | `/health` | Alle | Health check. |

Svarene har samme JSON-form som PublicApi havde, så BlazorAdmin ikke skulle ændres. `PictureUri` er en færdig URL: pladsholderen `http://catalogbaseurltobereplaced` erstattes af `CatalogBaseUrl`.

**Login:** Admin-endpoints kræver en JWT med rollen `Administrators`. Den udstedes af monolitten (Web's `UserController`, som BlazorAdmin henter den fra). CatalogService validerer den med den samme hemmelige nøgle (`Authorization/AuthorizationConstants.cs`).

**CORS:** BlazorAdmin kører i browseren på Web's adresse og kalder gatewayen på en anden adresse. Derfor tillader CatalogService Web's origin (`baseUrls:webBase`), og gatewayen sender `OPTIONS` videre.

Swagger UI: `http://localhost:5400/swagger`

## RabbitMQ

Efter hver ændring, når den er gemt, publicerer endpointet et event via `ICatalogEventPublisher`:

- **Exchange:** `catalog-events` (fanout, durable). Navnet ligger i `CatalogService.Contracts` (`CatalogEventsExchange.Name`).
- **Events:** `CatalogItemCreatedIntegrationEvent`, `CatalogItemUpdatedIntegrationEvent`, `CatalogItemDeletedIntegrationEvent`. Created og Updated har varens **fulde tilstand**, så en modtager bare kan overskrive sin kopi. Det gør det ligegyldigt, om et event kommer to gange.
- **Besked:** JSON (camelCase). `Type` = eventets klassenavn, og beskeden er `Persistent`.
- **Mønster:** publish/subscribe. CatalogService kender ikke modtagerne. En service, der vil have events, laver sin egen kø og binder den til exchangen. (Web abonnerede tidligere med køen `web.catalog-sync` for at holde sin katalog-kopi opdateret; den er fjernet sammen med kopien.)
- **Fejl:** Er RabbitMQ nede, logges fejlen, men ændringen er allerede gemt, og kaldet lykkes. Eventet går så tabt; et outbox-mønster ville løse det.

## Kør lokalt

```bash
# Uden SQL Server og RabbitMQ (in-memory database; events logges som fejl, men alt andet virker)
UseOnlyInMemoryDatabase=true dotnet run --project CatalogService/CatalogService.Api

# Med SQL Server LocalDB (databasen oprettes og seedes automatisk ved opstart)
dotnet run --project CatalogService/CatalogService.Api
```

Servicen kører på `http://localhost:5400` (https `5401`). Web finder den via `CatalogServiceBaseUrl` i `src/Web/appsettings.json`, og BlazorAdmin går gennem ApiGateway (`dotnet run --project src/ApiGateway --launch-profile https`).

Med Docker: kopiér `.env.example` til `.env` i roden og udfyld passwords. `docker compose up` starter `eshopcatalogservice` på port 5400. SQL- og RabbitMQ-passwords kommer fra `.env` via miljøvariablerne i `docker-compose.override.yml`, ligesom for de andre services.

Ved første opstart seedes de samme 12 varer, 5 mærker og 4 typer, som monolitten havde, så id'erne passer med det, kurv og ordrer allerede henviser til.

## Tests

```bash
dotnet test CatalogService/CatalogService.Tests
```

- **Domain:** regler i `CatalogItem` (fx at navn og pris ikke må være tomme).
- **Specifications:** filtrering og paging.
- **Endpoints:** hele HTTP-flowet mod en in-memory database, inkl. `401`/`403` uden token eller uden admin-rolle, `409` ved dubletnavn og at det rigtige event sendes. RabbitMQ er udskiftet med `FakeCatalogEventPublisher`, så testene ikke kræver en broker.

Uden for servicen bruger Web's functional tests `FakeCatalogApiClient` i stedet for den rigtige service, og checkout-testene (`tests/UnitTests/.../OrderCheckoutServiceTests`) mocker `ICatalogItemLookup`.

## Kendte begrænsninger

- Events kan gå tabt, hvis RabbitMQ er nede i det øjeblik, en vare ændres (intet outbox).
- Sletter man en vare, der ligger i en kurv, fejler kurvsiden og checkout i monolitten, fordi de forventer at finde varen i CatalogService (samme opførsel som før udtrækningen).
- Kurvsiden og checkout virker kun, når CatalogService kører. Varer hentes én ad gangen (parallelt), da CatalogService ikke har et "hent mange på id"-endpoint.
- JWT-nøglen er hardcodet og skal være den samme som monolittens (overtaget fra originalen).
