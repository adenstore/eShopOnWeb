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
ApiGateway ──── /api/baskets/*  ── commands ──▶ exchange "Basket" ──▶ BasketService
           └─── everything else ── Ocelot ────▶ CatalogService, OrderService, LagerService, PublicApi
```

## Basket routes

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

The reads (`Basket.getByBuyer`, `Basket.getById`) are not here yet — see "Still to do".

| Status | When |
|---|---|
| 202 | The broker acknowledged the command. |
| 400 | The command failed its own DataAnnotations, with the offending fields named. |
| 503 | The command could not be published. Nothing was queued; retrying is safe. |

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

## Running it

```sh
docker compose up -d rabbitmq
dotnet run --project src/ApiGateway     # http://localhost:5278
```

In compose the service is `eshopapigateway`, on port 5278.

Without BasketService running, the write routes answer 503 — correctly, since there is nothing to
publish to. To see a command land without starting the service, bind a queue to the `Basket`
exchange in the management UI at <http://localhost:15672> and watch `basket-service`.

## Tests

`tests/ApiGatewayTests` runs the gateway in memory with the publisher faked, which is enough to
cover what the gateway itself decides: which routing key, which command, which status code, that an
invalid body never reaches the queue, and that a broker failure is not reported as 202. It also
asserts a proxied route still gets an answer from Ocelot, because the pipeline ordering above is
easy to break and the failure mode is a silent 404.

That messages really route through a broker to BasketService is covered by
`BasketService.IntegrationTests` against a real RabbitMQ.

```sh
dotnet test tests/ApiGatewayTests
```

## Still to do

- **The read routes.** `GET /api/baskets/{buyerId}` and `GET /api/baskets/{basketId}` need a Redis
  projection fed by the `basket-events` fanout, plus request/reply over `amq.rabbitmq.reply-to` for
  checkout, where staleness is a money bug. Note the direct reply-to trap documented in
  `BasketService/README.md`: the request must be published on the very channel holding the reply-to
  consumer.
- **Nothing calls these routes yet.** The Web app still serves basket in-process; pointing
  `BasketServiceBaseUrl` at the gateway is a later step in the extraction.
