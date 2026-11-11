# CatalogService (microservice)

Katalog-delen af eShopOnWeb (varer, mærker og typer) er trukket ud af monolitten og kører som sin egen service med sin egen database (`Microsoft.eShopOnWeb.CatalogServiceDb`). Udtrækningen er lavet efter **strangler-mønsteret**: trafikken er flyttet over til den nye service, men monolittens gamle katalog står der stadig, indtil intet bruger det længere.

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
Web, forsiden ─────────REST (CatalogApiClient)──►      │
                                                       │ CatalogItemCreated/Updated/Deleted
                                                       ▼
                                      RabbitMQ exchange "catalog-events" (fanout)
                                                       │
                                                       ▼ kø "web.catalog-sync"
                              Web: CatalogEventsListener → CatalogEventSync → monolittens CatalogDb
                                                                                    ▲
                                                 kurv og checkout læser stadig herfra
```

- **BlazorAdmin** (admin-siderne) kalder gatewayen, som sender `/api/catalog-items`, `/api/catalog-items/{id}`, `/api/catalog-brands` og `/api/catalog-types` videre til CatalogService.
- **Web's forside** (produktlisten med filtre) læser fra CatalogService via `ICatalogApiClient`, når `"UseCatalogService": true`. Svarene caches som før af `CachedCatalogViewModelService`.
- **Kurv og checkout** i monolitten læser stadig varer fra monolittens egen `CatalogDb`. Den holdes opdateret af `Web/Messaging`, som lytter på CatalogService's events (se nedenfor).

## Hvad der stadig ligger i monolitten

| Hvad | Hvor | Hvorfor det ikke er fjernet endnu |
|---|---|---|
| Katalog-entiteter og -tabeller | `ApplicationCore/Entities`, `Infrastructure/Data/CatalogContext.cs` (tabellerne `Catalog`, `CatalogBrands`, `CatalogTypes` i `CatalogDb`) | Kurv og checkout læser dem (næste række). `CatalogDb` indeholder også kurvens tabeller, så databasen bliver under alle omstændigheder. |
| Opslag fra kurv og checkout | `Web/Pages/Basket/Index.cshtml.cs` (pris), `Web/Services/BasketViewModelService.cs` (navn, billede), `ApplicationCore/Services/OrderCheckoutService.cs` (snapshot til ordren) | Det er kurv-koden, som ejes af en anden i gruppen. Den skal over på `ICatalogApiClient`, før monolittens katalog kan slettes. |
| Synkronisering af `CatalogDb` | `Web/Messaging` (`CatalogEventsListener`, `CatalogEventSync`), slås til med `"SyncCatalogFromEvents": true` | Holder kopien opdateret, så kurven ser varer, der er oprettet eller rettet i CatalogService. Midlertidig bro, der skal slettes sammen med katalog-entiteterne. |
| Gammel visning af forsiden fra databasen | `Web/Services/CatalogViewModelService.cs`, bruges når `"UseCatalogService": false` | Fallback, så man kan skifte tilbage under migreringen. |
| PublicApi's katalog-endpoints | `src/PublicApi/Catalog*Endpoints` | Bliver ikke kaldt længere, fordi gatewayen sender katalog-ruterne til CatalogService. De er ladt stå for ikke at ændre i monolitten. |
| Gammel Razor-admin-side | `Web/Pages/Admin/EditCatalogItem` | Kan ikke nås og er kommenteret ud. Den skrev direkte i `CatalogDb` og ville derfor gå uden om CatalogService. |

Rækkefølgen for at blive helt færdig er: (1) kurv og checkout spørger CatalogService i stedet for `CatalogDb`, (2) sync og fallback slettes, (3) katalog-entiteterne slettes, og en migration fjerner de tre tabeller fra `CatalogDb`.

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
- **Mønster:** publish/subscribe. CatalogService kender ikke modtagerne. En service, der vil have events, laver sin egen kø og binder den til exchangen, som Web gør med `web.catalog-sync`.
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

Ved første opstart seedes de samme 12 varer, 5 mærker og 4 typer som i monolitten, så id'erne passer med det, kurv og ordrer allerede henviser til.

## Tests

```bash
dotnet test CatalogService/CatalogService.Tests
```

- **Domain:** regler i `CatalogItem` (fx at navn og pris ikke må være tomme).
- **Specifications:** filtrering og paging.
- **Endpoints:** hele HTTP-flowet mod en in-memory database, inkl. `401`/`403` uden token eller uden admin-rolle, `409` ved dubletnavn og at det rigtige event sendes. RabbitMQ er udskiftet med `FakeCatalogEventPublisher`, så testene ikke kræver en broker.

Uden for servicen tester `tests/IntegrationTests/Catalog/CatalogEventSyncTests.cs` synkroniseringen af monolittens `CatalogDb`, og Web's functional tests bruger `FakeCatalogApiClient` i stedet for den rigtige service.

## Kendte begrænsninger

- Events kan gå tabt, hvis RabbitMQ er nede i det øjeblik, en vare ændres (intet outbox).
- Sletter man en vare, der ligger i en kurv, fejler kurvsiden og checkout i monolitten, fordi de forventer at finde varen i `CatalogDb`. Det løses, når kurven spørger CatalogService og håndterer en manglende vare.
- JWT-nøglen er hardcodet og skal være den samme som monolittens (overtaget fra originalen).
