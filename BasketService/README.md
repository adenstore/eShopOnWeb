# BasketService

The basket microservice. It has **no REST API** — its interface is RabbitMQ. The only HTTP left is
`/health`.

That is the required architecture: the frontend talks REST to the API gateway, and everything from
the gateway inwards, plus all service-to-service traffic, goes over RabbitMQ.

```
Web (frontend)
  │  REST
  ▼
ApiGateway ──── commands + queries ───▶ exchange "Basket" (topic) ──▶ queue basket-service
     ▲                                                                      │
     └──── projection, fed by ◀── exchange "basket-events" (fanout) ◀────────┤
                                                                            │
OrderService ──▶ exchange "order-events" (fanout) ──▶ queue basket-order-events
```

## Topology

Declared by the service itself on startup (`Messaging/BasketCommandConsumer.cs`), so the broker
needs no manual setup. All names live in `Contracts/BasketMessaging.cs`.

| Exchange | Type | Purpose |
|---|---|---|
| `Basket` | topic | Commands and queries in. Has `alternate-exchange` → `Basket.unroutable`. |
| `basket-events` | fanout | `BasketChangedIntegrationEvent` out. |
| `Basket.invalid` | fanout | Dead-letter target for rejected messages. |
| `Basket.unroutable` | fanout | Catches messages whose routing key nothing is bound to. |

| Queue | Bound to | Notes |
|---|---|---|
| `basket-service` | `Basket` on all five routing keys | `x-dead-letter-exchange` → `Basket.invalid`, prefetch 1. |
| `basket-order-events` | `order-events` | Clears the basket after checkout. |
| `invalid` | `Basket.invalid` | Inspect this when a message disappears. |
| `unroutable-messages` | `Basket.unroutable` | A routing key nobody bound — usually a typo. |

## Messages

Writes are one-way; no caller uses a return value. Add-to-cart is already Post-Redirect-Get, so the
redirect threw away whatever the old REST call returned.

| Routing key | Message | Reply |
|---|---|---|
| `Basket.addItem` | `AddItemToBasketCommand` | none |
| `Basket.setQuantities` | `SetBasketQuantitiesCommand` | none |
| `Basket.transfer` | `TransferBasketCommand` | none |
| `Basket.getByBuyer` | `GetBasketByBuyerRequest` | `BasketQueryReply` |
| `Basket.getById` | `GetBasketByIdRequest` | `BasketQueryReply` |

Everything is JSON serialized with `JsonSerializerDefaults.Web` (camelCase), matching every other
publisher in the repo.

### Why request/reply exists at all

Only for the two reads, and only because checkout is blocked on the answer and staleness there is a
money bug — wrong items or wrong prices on a real order. It is deliberately *not* used for the
header item count, which renders on every page: that comes from a projection fed by
`basket-events`, so a page view costs no round-trip.

Commands and queries deliberately share one queue. A queue is FIFO and `basket-service` has a
single consumer with prefetch 1, so a query published after a command is handled after it. That is
what makes "I added an item, now show me my basket" correct without any version-stamping or waiting
on the read side.

### `BasketChanged` carries full state, not deltas

A fanout gives neither global ordering nor exactly-once delivery, so a delta stream would need
both. Full state is idempotent and self-healing: a lost event is repaired by the next one.

A reader should apply an event only if its `occurredAt` is at or after what it already holds for
that buyer — a redelivery can arrive behind a newer event and would otherwise resurrect stale items.

A deleted basket is announced as a `BasketChanged` with an empty item list, so a projection needs no
separate "deleted" case.

### Checkout clears the basket by itself

The old flow was SetQuantities → CreateOrder → DeleteBasket: three calls across two datastores with
no atomicity, where a crash could leave an order placed and the basket still full. Instead this
service subscribes to `order-events` and clears the basket when an order really exists. A
redelivery is a harmless no-op.

## Error handling

There is no caller to return a 400 to, so:

- **Malformed JSON or failed validation** → rejected, no requeue, lands on `invalid`.
  `MessageValidator` runs the DataAnnotations by hand; nothing invokes them for a queue consumer
  the way `[ApiController]` did for a controller.
- **A query that fails validation** still gets a reply, with `error` set — the caller is blocked
  waiting and would otherwise just time out.
- **An unhandled exception** → rejected without requeue. Redelivering a half-applied command would
  re-apply the committed part, and a deterministic failure would spin forever.
- **A publish failure on `basket-events`** is logged, not retried: the change is already committed,
  and replaying `AddItem` would add the quantity twice. The cost is a stale projection until that
  basket next changes. An outbox table is the fix if that ever shows up.
- **A broker that is not up yet** is retried with a doubling backoff rather than being fatal. An
  exception out of `BackgroundService.ExecuteAsync` stops the entire host by default, so a missing
  broker used to kill the service outright — `/health` included, leaving nothing to report why.
  `depends_on` in compose waits for the container to start, not for RabbitMQ to finish booting, so
  that race is routine. Once attached, RabbitMQ.Client recovers dropped connections, channels and
  consumers by itself.

## Calling it: the direct reply-to trap

With `amq.rabbitmq.reply-to`, the request **must** be published on the same channel that holds the
reply-to consumer. On any other channel the broker tears the channel down with
`406 PRECONDITION_FAILED - fast reply consumer does not exist`, and `BasicPublishAsync` does *not*
throw — so it looks like the request vanished.

Use one channel per in-flight request, or one long-lived channel that both publishes requests and
consumes replies.

## Running it

```sh
docker compose up -d rabbitmq sqlserver
dotnet ef database update --project BasketService/BasketService --startup-project BasketService/BasketService
dotnet run --project BasketService/BasketService
```

The `nix develop` shell exports `ConnectionStrings__BasketConnection` and the RabbitMQ credentials.
Both matter:

- `appsettings.json` still points at `(localdb)\mssqllocaldb`, which does not exist on Linux.
- Setting `RABBITMQ_DEFAULT_USER` in `docker-compose.yml` *replaces* the `guest` account rather than
  adding to it, so the `guest`/`guest` default in `appsettings.json` cannot authenticate against
  that broker at all. Use `eshop`/`eshop`.

In compose the service is `eshopbasketservice`, with `/health` on port 5198.

Watch it work in the management UI at <http://localhost:15672> (`eshop`/`eshop`): bind a scratch
queue to `basket-events` to see the events, and check `invalid` when a message goes missing.

## Tests

Two projects, at two levels of the test pyramid.

**`BasketService.Tests` — 48 tests, no broker, milliseconds.** Every message rule lives in
`Messaging/BasketMessageHandler.cs`, which has no AMQP in it; the consumers above it are pure
transport. That split is what keeps the rules reachable without infrastructure.
`Contracts/WireFormatTests.cs` round-trips each message shape, including
`OrderCreatedIntegrationEvent` from OrderService's own contracts assembly, because a shape that does
not round-trip fails at runtime on a queue where there is no compiler to catch it.

**`BasketService.IntegrationTests` — 15 tests, real RabbitMQ, ~15s. Needs Docker.** Testcontainers
starts an actual broker on a random port and `WebApplicationFactory` runs the service against it,
with only the database faked. These cover what a unit test cannot: that messages actually route,
that rejected ones reach the dead-letter queue, that an unroutable key is caught by the alternate
exchange, and that request/reply works over the broker rather than over a method call.

Because messaging is not request/response, those tests assert through `PolledWait` — a Polly retry
that re-checks until the assertion holds. Asserting immediately after a publish would measure the
speed of the machine, not the behaviour of the service: green on a laptop, flaky in CI.

```sh
dotnet test BasketService/BasketService.Tests              # always
dotnet test BasketService/BasketService.IntegrationTests   # needs a container runtime
```

## Still to do

- **The gateway.** Nothing publishes to these exchanges yet. The Web app still serves basket
  in-process (see `ConfigureCoreServices.cs`); `BasketApiClient` is kept, unregistered, to become
  the frontend→gateway client. Ocelot cannot do this — it is an HTTP proxy with no AMQP transport —
  so these become hand-written endpoints that publish.
- **The read projection**, planned for Redis so it survives a gateway restart and works with more
  than one gateway instance.
- **Its own database.** `BasketConnection` points at a separate `BasketDb` but the monolith still
  owns the live basket tables in the catalog DB, and still serves all basket traffic.
- `IBasketQueryService` / `Data/Queries/BasketQueryService.cs` are now unreachable: the item count
  rides along on `BasketSnapshot.TotalItems` so that readers need no second round-trip. Left in
  place rather than deleted as part of a transport change.
- Denormalising product name and picture onto `BasketSnapshot` would let the Web layer stop reading
  catalog tables to render a basket, but it needs a catalog projection in this service fed by
  `catalog-events`, plus a decision about what happens to an item already in someone's basket when
  the catalog deletes it. Adding those fields later is additive, not breaking.
- `/health` currently verifies nothing — not the database, not the broker connection. For a service
  whose real interface is a queue, a check that it is actually consuming would be worth more.
