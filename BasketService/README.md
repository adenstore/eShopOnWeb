# BasketService

Kurvens microservice. Den har **intet REST-API** — dens grænseflade er RabbitMQ. Det eneste HTTP,
der er tilbage, er `/health`.

Det er den påkrævede arkitektur: frontenden taler REST med API-gatewayen, og alt fra gatewayen og
indad, inklusive al trafik mellem services, går over RabbitMQ.

```
Web (frontend)
  │  REST
  ▼
ApiGateway ──── kommandoer + forespørgsler ───▶ exchange "Basket" (topic) ──▶ kø basket-service
     ▲                                                                               │
     └──── projektion, fodret af ◀── exchange "basket-events" (fanout) ◀─────────────┤
                                                                                     │
OrderService ──▶ exchange "order-events" (fanout) ─────────────────────▶ kø basket-order-events
```

## Topologi

Erklæres af servicen selv ved opstart (`Messaging/BasketCommandConsumer.cs`), så brokeren kræver
ingen manuel opsætning. Alle navne ligger i `Contracts/BasketMessaging.cs`.

| Exchange | Type | Formål |
|---|---|---|
| `Basket` | topic | Kommandoer og forespørgsler ind. Har `alternate-exchange` → `Basket.unroutable`. |
| `basket-events` | fanout | `BasketChangedIntegrationEvent` ud. |
| `Basket.invalid` | fanout | Dead-letter-mål for afviste beskeder. |
| `Basket.unroutable` | fanout | Opfanger beskeder, hvis routing key intet er bundet til. |

| Kø | Bundet til | Noter |
|---|---|---|
| `basket-service` | `Basket` på alle fem routing keys | `x-dead-letter-exchange` → `Basket.invalid`, prefetch 1. |
| `basket-order-events` | `order-events` | Tømmer kurven efter checkout. |
| `invalid` | `Basket.invalid` | Kig her, når en besked forsvinder. |
| `unroutable-messages` | `Basket.unroutable` | En routing key, ingen har bundet — som regel en tastefejl. |

## Beskeder

Skrivninger er envejs; ingen kalder bruger en returværdi. Læg-i-kurv er i forvejen
Post-Redirect-Get, så redirectet smed alligevel væk, hvad det gamle REST-kald returnerede.

| Routing key | Besked | Svar |
|---|---|---|
| `Basket.addItem` | `AddItemToBasketCommand` | intet |
| `Basket.setQuantities` | `SetBasketQuantitiesCommand` | intet |
| `Basket.transfer` | `TransferBasketCommand` | intet |
| `Basket.getByBuyer` | `GetBasketByBuyerRequest` | `BasketQueryReply` |
| `Basket.getById` | `GetBasketByIdRequest` | `BasketQueryReply` |

Alt serialiseres som JSON med `JsonSerializerDefaults.Web` (camelCase), magen til alle andre
publishers i repoet.

### Hvorfor request/reply overhovedet findes

Kun til de to læsninger, og kun fordi checkout er blokeret på svaret, og forældede data dér er en
fejl, der koster penge — forkerte varer eller forkerte priser på en rigtig ordre. Det bruges
bevidst *ikke* til antallet af varer i headeren, som renderes på hver eneste side: det kommer fra
en projektion fodret af `basket-events`, så et sidevisning koster ingen round-trip.

Kommandoer og forespørgsler deler bevidst én kø. En kø er FIFO, og `basket-service` har én enkelt
consumer med prefetch 1, så en forespørgsel, der publiceres efter en kommando, behandles efter den.
Det er dét, der gør "jeg lagde en vare i kurven, vis mig så min kurv" korrekt uden nogen form for
versionsstempling eller venten på læsesiden.

### `BasketChanged` bærer hele tilstanden, ikke deltaer

En fanout giver hverken global rækkefølge eller præcis-én-gang-levering, så en strøm af deltaer
ville kræve begge dele. Hele tilstanden er idempotent og retter sig selv: et tabt event bliver
repareret af det næste.

En læser bør kun anvende et event, hvis dets `occurredAt` er på eller efter det, læseren allerede
har for den køber — en genlevering kan ankomme bag et nyere event og ville ellers genoplive
forældede varer.

En slettet kurv annonceres som et `BasketChanged` med en tom vareliste, så en projektion ikke
behøver et separat "slettet"-tilfælde.

### Checkout tømmer selv kurven

Det gamle flow var SetQuantities → CreateOrder → DeleteBasket: tre kald på tværs af to datalagre
uden atomicitet, hvor et nedbrud kunne efterlade en oprettet ordre og en stadig fyldt kurv. I
stedet abonnerer denne service på `order-events` og tømmer kurven, når en ordre rent faktisk
findes. En genlevering er en harmløs no-op.

## Fejlhåndtering

Der er ingen kalder at returnere en 400 til, så:

- **Ugyldig JSON eller fejlet validering** → afvises, ingen requeue, lander på `invalid`.
  `MessageValidator` kører DataAnnotations i hånden; intet kalder dem for en kø-consumer, sådan som
  `[ApiController]` gjorde for en controller.
- **En forespørgsel, der fejler validering**, får stadig et svar, med `error` sat — kalderen venter
  blokeret og ville ellers bare løbe ud i en timeout.
- **En uhåndteret exception** → afvises uden requeue. At genlevere en halvt anvendt kommando ville
  genanvende den del, der allerede er committet, og en deterministisk fejl ville køre i ring for
  evigt.
- **En fejlet publish på `basket-events`** logges, men prøves ikke igen: ændringen er allerede
  committet, og at afspille `AddItem` igen ville lægge antallet i kurven to gange. Prisen er en
  forældet projektion, indtil den kurv ændres næste gang. En outbox-tabel er løsningen, hvis det
  nogensinde bliver et problem.
- **En broker, der ikke er oppe endnu**, prøves igen med fordoblende backoff i stedet for at være
  fatal. En exception ud af `BackgroundService.ExecuteAsync` stopper som udgangspunkt hele hosten,
  så en manglende broker plejede at slå servicen helt ihjel — `/health` inklusive, så der ikke var
  noget tilbage til at fortælle hvorfor. `depends_on` i compose venter på, at containeren starter,
  ikke på at RabbitMQ bliver færdig med at boote, så det kapløb er helt normalt. Når først
  forbindelsen er etableret, genopretter RabbitMQ.Client selv tabte forbindelser, kanaler og
  consumers.

## At kalde den: direct reply-to-fælden

Med `amq.rabbitmq.reply-to` **skal** forespørgslen publiceres på den samme kanal, som holder
reply-to-consumeren. På enhver anden kanal river brokeren kanalen ned med
`406 PRECONDITION_FAILED - fast reply consumer does not exist`, og `BasicPublishAsync` kaster
*ikke* — så det ser ud, som om forespørgslen forsvandt.

Brug én kanal pr. forespørgsel undervejs, eller én langtidslevende kanal, der både publicerer
forespørgsler og modtager svar.

## At køre den

```sh
docker compose up -d rabbitmq sqlserver
dotnet run --project BasketService/BasketService
```

Servicen opretter selv `Microsoft.eShopOnWeb.BasketDb` og kører sine migrations ved opstart, så der
er ikke noget `dotnet ef database update`-trin — og intet afhænger af, at nogen huskede at køre et.
Det sker før `app.Run()`, som er dét, der starter consumerne, så denne service forbruger aldrig en
kommando, den ikke kan committe; en kommando er envejs, så en, der fejlede på en manglende tabel,
ville blive dead-letteret til `invalid` uden nogen tilbage at fortælle det til.

Den prøver igen i op til to minutter, mens den venter på SQL Server. Det er ikke livrem og seler:
`compose up` returnerer, så snart *containeren* er startet, og `azure-sql-edge`-imaget bruger cirka
40 sekunder derefter på at tage imod forbindelser, hvor TCP'ens pre-login-handshake fejler på en
måde, som `EnableRetryOnFailure()` ikke behandler som forbigående. Uden den retry døde servicen
under opstart. Se `Data/DatabaseMigrator.cs`.

Sådan tilføjer du en migration (`--startup-project` er påkrævet — ellers får `docker-compose.dcproj`
i repoets rod `dotnet-ef` til at crashe på et tomt `frameworkName`):

```sh
dotnet ef migrations add <Navn> --project BasketService/BasketService --startup-project BasketService/BasketService
```

Når du kører servicen lokalt, skal du selv sætte `ConnectionStrings__BasketConnection` og
RabbitMQ-brugeroplysningerne som environment variables. Begge dele betyder noget:

- `appsettings.json` peger stadig på `(localdb)\mssqllocaldb`, som ikke findes på Linux.
- At sætte `RABBITMQ_DEFAULT_USER` i `docker-compose.yml` *erstatter* `guest`-kontoen i stedet for
  at lægge til, så `guest`/`guest`-standarden i `appsettings.json` slet ikke kan autentificere mod
  den broker. Brug `eshop`/`eshop`.

I compose hedder servicen `eshopbasketservice`, med `/health` på port 5198.

Følg med i hvad der sker i management-UI'et på <http://localhost:15672> (`eshop`/`eshop`): bind en
midlertidig kø til `basket-events` for at se eventene, og tjek `invalid`, når en besked forsvinder.

## Tests

To projekter, på to niveauer af testpyramiden.

**`BasketService.Tests` — 48 tests, ingen broker, millisekunder.** Alle beskedregler ligger i
`Messaging/BasketMessageHandler.cs`, som ikke har AMQP i sig; consumerne ovenover den er ren
transport. Netop den opdeling er dét, der holder reglerne tilgængelige uden infrastruktur.
`Contracts/WireFormatTests.cs` round-tripper hver beskedtype, inklusive
`OrderCreatedIntegrationEvent` fra OrderServices eget contracts-assembly, fordi en type, der ikke
round-tripper, fejler ved kørsel på en kø, hvor der ingen compiler er til at fange det.

**`BasketService.IntegrationTests` — 15 tests, rigtig RabbitMQ, ~15 s. Kræver Docker.**
Testcontainers starter en ægte broker på en tilfældig port, og `WebApplicationFactory` kører
servicen mod den, hvor kun databasen er fakede. De dækker det, en unit test ikke kan: at beskeder
rent faktisk bliver routet, at afviste beskeder når dead-letter-køen, at en uroutbar key fanges af
den alternative exchange, og at request/reply virker over brokeren frem for over et metodekald.

Fordi messaging ikke er request/response, asserterer de tests gennem `PolledWait` — en Polly-retry,
der tjekker igen, indtil assertionen holder. At assertere umiddelbart efter en publish ville måle
maskinens hastighed, ikke servicens opførsel: grøn på en bærbar, ustabil i CI.

```sh
dotnet test BasketService/BasketService.Tests              # altid
dotnet test BasketService/BasketService.IntegrationTests   # kræver en container-runtime
```

## Mangler stadig

- **Ingen automatiseret end-to-end-test af den rigtige stak.** Cut-over blev verificeret i hånden
  den 17. november 2026 — flere varer i forskellige antal lagt i kurven og gennemført køb mod en
  genbygget compose-stak — og monolittens kurv blev slettet på baggrund af den kørsel. Det, der
  stadig mangler, er en *test*: suiten er grøn, og
  `tests/UnitTests/Infrastructure/Services/BasketApiClientTests.cs` låser den REST-kontrakt fast,
  som Web-appen sender, men de funktionelle tests indsætter `FakeBasketApiClient`, så intet sender
  REST ind i frontenden og asserterer, at en kurv kommer ud af en rigtig broker og en rigtig
  service. Faken anvender bevidst skrivninger synkront, så den kan ikke fange en tabt
  read-your-own-writes-garanti — og det er dér, hullet gør ondt.
- Denne services egne `Interfaces/IBasketQueryService.cs` / `Data/Queries/BasketQueryService.cs` er
  uopnåelige: antallet af varer følger med på `BasketSnapshot.TotalItems`, så læsere ikke behøver
  en ekstra round-trip. Efterladt frem for slettet som led i en transportændring.
- At denormalisere produktnavn og billede ind på `BasketSnapshot` ville lade Web-laget holde op med
  at læse katalogtabeller for at rendere en kurv, men det kræver en katalogprojektion i denne
  service fodret af `catalog-events`, plus en beslutning om, hvad der sker med en vare, der
  allerede ligger i nogens kurv, når kataloget sletter den. At tilføje de felter senere er
  additivt, ikke brydende.
- `/health` verificerer i dag ingenting — hverken databasen eller brokerforbindelsen. For en
  service, hvis egentlige grænseflade er en kø, ville et tjek af, at den faktisk forbruger
  beskeder, være mere værd.
