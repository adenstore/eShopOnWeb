# ApiGateway

The single REST entry point the frontend talks to. It does its job in two different ways:

- **Proxying.** The services that still speak HTTP — CatalogService, OrderService, LagerService and
  the old PublicApi — are reverse-proxied by Ocelot, configured in `ocelot.json`.
- **Publishing.** Basket is not proxied. BasketService has no REST API at all; its interface is
  RabbitMQ. So the gateway terminates REST here and publishes a message instead.

The second one is the required architecture: REST from the frontend to the gateway, messages from
the gateway inwards. Ocelot cannot do it — it is an HTTP reverse proxy with no AMQP transport — so
those routes are hand-written endpoints in `Endpoints/BasketEndpoints.cs`.

```
Web (frontend)
  │  REST
  ▼
ApiGateway ──── POST/PUT /api/baskets/*  ── commands ──▶ exchange "Basket" ──▶ BasketService
           │                                                                        │
           ├─── GET /api/baskets/*  ◀── projection ◀── Redis ◀── "basket-events" ◀──┘
           │                        └── or a query ──▶ exchange "Basket" ──────────▶ (same queue)
           │
           └─── everything else ── Ocelot ────▶ CatalogService, OrderService, LagerService, PublicApi
```

## Basket write routes

All three are **one-way commands**, and all three answer **202 Accepted**: the broker has the
command, the basket has not changed yet. No caller needs a result — add-to-cart is already
Post-Redirect-Get, so the redirect re-reads the basket anyway.

| Route | Body | Published as |
|---|---|---|
| `POST /api/baskets/{buyerId}/items` | `{ "catalogItemId": 1, "unitPrice": 9.99, "quantity": 2 }` | `Basket.addItem` |
| `PUT /api/baskets/{basketId}/items` | `{ "quantities": { "17": 3, "18": 0 } }` | `Basket.setQuantities` |
| `POST /api/baskets/transfer` | `{ "anonymousId": "…", "userName": "…" }` | `Basket.transfer` |

The buyer and the basket id live in the URL, where a REST resource keeps its identity; the endpoint
combines them with the body into the command it publishes. There is deliberately no `DELETE`:
BasketService clears the basket itself when it sees OrderService's `OrderCreated` event.

| Status | When |
|---|---|
| 202 | The broker acknowledged the command. |
| 400 | The command failed its own DataAnnotations, with the offending fields named. |
| 503 | The command could not be published. Nothing was queued; retrying is safe. |

## Basket read routes

| Route | Answers | Read from |
|---|---|---|
| `GET /api/baskets/{buyerId}` | `BasketSnapshot` | projection, or a query if it cannot answer |
| `GET /api/baskets/{buyerId}/items/count` | `{ "count": 3 }` | the same read as above |
| `GET /api/baskets/{basketId}` | `BasketSnapshot` | always a query |

| Status | When |
|---|---|
| 200 | A basket. By buyer, an empty one with id `0` when they have none. |
| 404 | By basket id only: that basket does not exist. |
| 502 | BasketService answered with an error rather than a basket. |
| 503 | The query could not be published at all. |
| 504 | The query went out and nothing came back in time. |

Three decisions worth knowing about:

- **`GET /api/baskets/{buyerId}` answers an empty basket, not 404.** Baskets are created lazily on
  the first add, so for every first-time visitor "no basket" is the normal state — a 404 would make
  the common path an error path. The basket id is `0` in that case, and a caller keying off the id
  has to read `0` as "nothing to update yet".
- **`GET /api/baskets/{basketId}` never touches the projection.** It is the read checkout builds an
  order from, where stale items or stale prices are a money bug rather than a cosmetic one. It also
  happens once per order rather than once per page, so it can afford the round-trip. A missing
  basket is a 404 here, because answering an empty basket would sail past the caller's null check
  and become an order with no items.
- **The int route constraint is what separates the two.** Endpoint routing prefers the constrained
  segment, so `{basketId:int}` and `{buyerId}` never collide — at the cost that a buyer id made
  only of digits would land on the wrong one. A buyer id in this shop is a cookie GUID or an email
  address, so that cannot happen.

### The projection

`Projections/` keeps a copy of every basket in Redis, fed by `Messaging/BasketEventsConsumer` from
the `basket-events` fanout. It exists because of one route: the item count is a ViewComponent in the
shared layout, so it renders on **every page of the shop**, and a query there would put a broker
round-trip on every page view.

It is an accelerator, never the authority. It answers one of three things, and only the first two
are answers: *here is the basket*, *this buyer has no basket*, or *ask BasketService*. Four rules
keep that honest, each for a specific way a projection goes wrong:

| Rule | Without it |
|---|---|
| Events apply only if not older than what is stored | a redelivery after a restart resurrects removed items |
| A buyer's own in-flight writes are counted, and reads are served by a query while the count is above zero | add-to-cart redirects to a page that does not have the item yet |
| A "no basket" answer is cached, but can never overwrite a projected basket | every page view by a shopper who has added nothing costs a round-trip |
| A projection that cannot be reached reads as "ask BasketService" | Redis being down takes basket pages down with it |

The in-flight count is the read-your-own-writes guard, and it is **cleared by the event the command
produced**, not by a timer — so the round-trip stops as soon as the projection has really caught up.
The expiry on it is only a backstop for commands that produce no event at all: a `SetQuantities` for
a basket already checked out, a transfer with nothing to transfer, or an event BasketService failed
to publish. It is counted rather than flagged because a shopper can click "add" twice, and the first
event only proves the projection has caught up with the first command.

What makes the fallback a real answer rather than a guess is the queue, not a timeout: commands and
queries share `basket-service`, which has a single consumer with a prefetch of 1, so a query
published after a command is always handled after that command has committed.

Deliberately **not** done: serving a known-stale basket when the query fails. The projection
declined for a reason — usually that this shopper has just changed their basket — and showing them
their previous basket as though it were current is the "I added it and it vanished" bug the guard
exists to prevent. A 503 says so instead.

Redis holds three kinds of key, all under `basket:` so they do not collide with LagerService's
`lager:` stock in the same Redis: `basket:{buyerId}` (the snapshot and its event timestamp),
`basket:owner:{basketId}` (so the `PUT` route, which only gets a basket id, can find whose reads to
guard) and `basket:pending:{buyerId}` (the in-flight count). The compare-and-write rules run as Lua
inside Redis, because each of them is a check and a write that must not have a gap between them —
`LagerService/Data/RedisStockRepository.cs` does the same for its stock.

### Why the queries are request/reply

`Messaging/BasketQueryClient` publishes to the `Basket` exchange with `ReplyTo` set to
`amq.rabbitmq.reply-to` and waits, on a channel per query. That last part is not an accident: with
direct reply-to the request **must** be published on the very channel holding the reply-to consumer,
or the broker tears that channel down with `406 PRECONDITION_FAILED - fast reply consumer does not
exist` — and `BasicPublishAsync` does not throw, so it presents as a request that silently vanished.
The alternative, one long-lived channel multiplexing replies by correlation id, is worth revisiting
only if the projection stops absorbing most reads.

That channel has publisher confirmations on, like the command publisher's, and here it buys one more
thing: it is what separates *could not ask* from *got no answer*. Against a broker with no `Basket`
exchange — BasketService owns it, so it does not exist until the service has started once — the read
route answered 504 after the full 5s timeout without confirmations, and 503 in milliseconds with
them.

### Why validate here as well as in BasketService

Both sides run the same annotations, and that is not duplication for its own sake. BasketService
validates because a queue has no caller to complain to: an invalid command is dead-lettered to the
`invalid` queue and the shopper just sees nothing happen. The gateway validates because it still
*has* a caller, and 400 with the field name is an answer the frontend can act on.

### Why the publish is confirmed

RabbitMQ.Client leaves publisher confirmations off by default, which makes `BasicPublishAsync`
fire-and-forget: a missing exchange comes back as an asynchronous channel error long after the call
returned "successfully". The channel is created with confirmations and confirmation tracking on, so
the publish waits for the broker's ack and throws otherwise. Without that, a 202 from this gateway
would mean nothing — verified by pointing it at a broker with no `Basket` exchange and watching it
answer 503 instead of 202.

The gateway does **not** declare the exchange. BasketService owns that topology, including the
`alternate-exchange` argument on `Basket`, and re-declaring the same exchange with different
arguments is a channel-killing `406 PRECONDITION_FAILED`. The consequence is deliberate: if
BasketService has never started, there is no exchange, and the caller gets a 503 instead of a 202
for a command nothing will ever read.

## The Ocelot ordering trap

`UseOcelot()` is terminal middleware — it answers the request itself and never calls what comes
after it — and `ocelot.json` ends with a catch-all `/api/{everything}` route, so Ocelot has an
answer for everything. Anything the gateway serves itself therefore has to run **before** it:

```csharp
app.UseRouting();
app.UseEndpoints(endpoints => endpoints.MapBasketEndpoints());
await app.UseOcelot();
```

`UseEndpoints` calls the next middleware when no endpoint matched, which makes Ocelot the fallback
for everything the gateway does not serve itself — and means the next route moved off Ocelot needs
no changes here. Top-level `app.MapPost(...)` registrations (what analyzer ASP0014 suggests) do
**not** work: the framework appends their endpoint middleware at the very end of the pipeline,
behind Ocelot, which never gets there.

## Configuration

`ocelot.json` routes to the compose hostnames; `ocelot.Development.json` routes to localhost ports.
Exactly one of them is loaded, because .NET merges JSON arrays item by item and loading both would
interleave the two route lists.

RabbitMQ is configured from the `RabbitMQ` section, the same shape every service uses, so one
compose environment block configures them all. The `guest`/`guest` default in `appsettings.json`
only works against a broker running on the same machine — `docker-compose.yml` *replaces* the guest
account with `RABBITMQ_DEFAULT_USER`, so a containerised broker needs `eshop`/`eshop`
(`appsettings.Docker.json`, and the `nix develop` shell exports the same for a local run).

Redis comes from `ConnectionStrings:Redis` (`redis:6379` in compose, `localhost:6379` otherwise).
`Projection:UseOnlyInMemoryStore` swaps it for an in-process projection, as LagerService's
`UseOnlyInMemoryDatabase` does for its stock — fine for a local run, not a real projection, since it
is lost on restart and not shared between instances.

Neither connection is opened at startup. The broker is routinely not ready yet when compose starts
the gateway, and a gateway that refused to boot would also stop proxying the services that have
nothing to do with RabbitMQ or Redis.

## Running it

```sh
docker compose up -d rabbitmq redis
dotnet run --project src/ApiGateway     # http://localhost:5278
```

In compose the service is `eshopapigateway`, on port 5278.

Without BasketService running, the write routes answer 503 — correctly, since there is nothing to
publish to — and the read routes answer 504 once the query times out. To see a command land without
starting the service, bind a queue to the `Basket` exchange in the management UI at
<http://localhost:15672> and watch `basket-service`.

## Tests

`tests/ApiGatewayTests` runs the gateway in memory with the broker faked and needs no
infrastructure. It covers what the gateway itself decides: which routing key, which command, which
status code, that an invalid body never reaches the queue, that a broker failure is not reported as
202 — and on the read side, **where each answer came from**, since a test that only checked the
response body would pass just as happily with no projection at all. It also asserts a proxied route
still gets an answer from Ocelot, because the pipeline ordering above is easy to break and the
failure mode is a silent 404.

The projection in those tests is the real `InMemoryBasketProjection`, not a stub, so the rules the
read endpoints are built on are the ones under test.

`tests/ApiGatewayIntegrationTests` runs the same rules against a real Redis in a container
(Testcontainers, as `BasketService.IntegrationTests` does for RabbitMQ). That is where the Lua is
proven: the in-memory projection re-implements the rules in C#, so only this suite says the
transcription is faithful. Both mutations worth worrying about — dropping the newer-wins compare and
unclamping the in-flight decrement — fail it.

That messages really route through a broker to BasketService is covered by
`BasketService.IntegrationTests`.

```sh
dotnet test tests/ApiGatewayTests              # no infrastructure
dotnet test tests/ApiGatewayIntegrationTests   # needs Docker or Podman
```

## Still to do

- **End-to-end verification of the cut-over.** The Web app calls these routes now:
  `BasketServiceBaseUrl` points at the gateway and `BasketApiClient` is registered for both basket
  interfaces (`ConfigureCoreServices.cs`). The build and the full suite are green, but nothing has
  yet driven a real browser through a real gateway, broker and service — which is where the
  interesting failures live, because they are all timing. Until that is done the monolith's
  in-process basket is left compiling but unregistered, so reverting is a one-line change.
  <br>
  Both traps the plan called out are closed rather than avoided: the application uses
  `BasketSnapshot` itself instead of a DTO that renames `basketId` to `id` (which would have read
  `0` silently, indistinguishable from a buyer with no basket), and `IBasketService.NoBasketId`
  plus `BasketViewModel.HasBasket` give the two call sites that key off the id — basket update and
  checkout — somewhere to branch before they address basket `0`.
- **No end-to-end test through the gateway to BasketService.** The read path is covered on both
  sides of the boundary — the gateway's decisions here, the message contracts in
  `BasketService.IntegrationTests` — but nothing yet drives REST in at one end and asserts a basket
  comes back out of a real broker and a real service.
