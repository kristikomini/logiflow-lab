# 6. Pushing to a browser: SignalR, and where it belongs

> Part of [Module 25 — Distributed systems and integration](README.md).
> Previous: [5. Sagas and eventual consistency](05-sagas-and-eventual-consistency.md) ·
> Next: [7. gRPC, and when it is the right answer](07-grpc.md)

---

Everything else in this module is about services talking to each other. This chapter is about the
one consumer that cannot be redeployed with you and cannot be asked to poll politely: a browser
somebody has open.

The warehouse screen wants to know when an order ships. There are exactly three ways it can find
out, and only one of them is interesting.

| | How | What it costs |
| --- | --- | --- |
| **Polling** | `setInterval(fetch, 5000)` | Simple, works everywhere, and wrong twice: a five-second lie on every change, and 720 requests per hour per open tab that mostly return "no news" |
| **Long polling** | request, server holds it open until something happens | Timely, and it holds a connection per client anyway — which is the cost people think they are avoiding |
| **A persistent connection** | WebSocket | Timely and cheap per message, at the price of a stateful connection your infrastructure has to keep alive |

SignalR is the third one with the first two as fallbacks, plus reconnection, plus a message
protocol, plus server-to-client method dispatch. **On a modern network it is a WebSocket.** What
you are buying is not the WebSocket — that is 20 lines — it is everything around it that you would
otherwise write badly: transport negotiation, reconnect with backoff, and a way to address one
client rather than all of them.

## The four ways to address a client

```csharp
Clients.All                    // everyone connected. The demo. Almost never right.
Clients.Caller                 // just the connection that invoked this method
Clients.User(userId)           // every connection belonging to one user — laptop and phone
Clients.Group("order-41")      // everyone who asked to watch order 41
```

`Clients.All` is what every tutorial uses and it is a data leak waiting to be written up: a screen
watching order 41 has no business receiving order 42's shipping address. `OrderTrackingHub` uses
groups, joined explicitly by the client:

```csharp
public Task SubscribeToOrder(Guid orderId) =>
    Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(orderId), Context.ConnectionAborted);
```

A group is just a name. It is created on the first join, disappears on the last leave, and costs
nothing while empty — so per-order groups are not extravagant, they are the cheap option.

**The group name has to be built in one place.** The hub joins `order-{id}`; the publisher sends to
`order-{id}`. If those two strings ever disagree — a capital letter, a dash instead of a colon —
the publisher sends to a group nobody is in and the subscriber waits in a group nobody sends to,
and **neither side logs anything at all**. Hence `OrderTrackingHub.GroupFor`, which exists purely
so there is only one string.

## Where the push comes from, and why not from the handler

The obvious place to call the hub is in the command handler, right after the order ships. Do not.

An order ships inside a transaction. If you push from the handler and the transaction then rolls
back, you have told a browser about something that did not happen — and there is no message you can
send that takes it back, because the user already read it.

So the push comes from the **outbox**, which is the same answer this codebase gives for every
"tell somebody else about it" problem:

```
handler → domain event → outbox row  ← same transaction, commits or does not
                             │
                    OutboxProcessor polls
                             │
              IIntegrationEventPublisher.PublishAsync
                             │
                    ┌────────┴────────┐
            log line (default)   SignalR push (when the API is running)
```

`IIntegrationEventPublisher` lives in **Application**, is implemented by a log line in
**Infrastructure**, and is implemented again by `SignalRIntegrationEventPublisher` in **Api** — the
only project allowed to know SignalR exists. The outbox processor calls the interface and never
learns which one it got. That is the dependency rule doing something load-bearing rather than
decorative.

The API registers its implementation *after* `AddInfrastructure`, and the later registration wins:

```csharp
builder.Services.AddScoped<IIntegrationEventPublisher, SignalRIntegrationEventPublisher>();
```

> **The DI rule worth memorising.** For a single `GetRequiredService<T>()`, the **last**
> registration wins. For `IEnumerable<T>`, you get **all** of them, in registration order. So
> "registered twice" is a bug in one shape and a feature in the other, and which one you are in
> depends entirely on how the consumer asks.

## Do not push the domain event

```csharp
// No.
await hub.Clients.Group(g).OrderStatusChanged(orderSubmittedDomainEvent);
```

Two reasons, and the second is the one that bites later:

1. `OrderSubmittedDomainEvent` carries `Money`, `Weight` and the customer's `Address`. The browser
   asked for a status string.
2. The moment it crosses the wire, that record's property names are a **public API**. Renaming a
   field in your own domain now breaks a client you cannot deploy.

`OrderStatusUpdate` is the translation, and writing it by hand is the point — an anti-corruption
layer you can skip is one you will skip.

Note which timestamp it carries: `OccurredAtUtc` from the event, not `UtcNow` at send time. The
outbox polls on a timer and can be minutes behind after an outage, so "when it happened" and "when
you were told" are genuinely different numbers, and a UI that shows the second as the first is
lying in exactly the situation where someone is looking closely.

## Two things that will catch you

### The token cannot travel in a header

The browser's WebSocket API takes a URL and a subprotocol list. **It cannot set request headers.**
So `Authorization: Bearer …` is not available on the handshake, and SignalR's JavaScript client
puts the token in the query string as `access_token` instead — where nothing reads it unless you
say so:

```csharp
options.Events = new JwtBearerEvents
{
    OnMessageReceived = context =>
    {
        if (string.IsNullOrEmpty(context.Token)
            && context.Request.Path.StartsWithSegments("/hubs", StringComparison.OrdinalIgnoreCase)
            && context.Request.Query.TryGetValue("access_token", out StringValues token))
        {
            context.Token = token;
        }

        return Task.CompletedTask;
    },
};
```

The symptom without it is a hub returning 401 to a client whose token works on every REST endpoint,
which sends people to look at CORS for an afternoon.

Keep the exception narrow — `/hubs` only, and only when the header is absent. A token in a URL is a
token in the access log, in the browser history, and in every proxy in between. That is acceptable
for a handshake with no alternative and nowhere else.

### One process, one set of connections

A connection lives in the memory of the instance that accepted it. Two instances behind a load
balancer, and an event handled by instance A reaches nobody connected to instance B — with a
completely healthy log on both.

The fix is a **backplane**: every instance publishes every message to Redis, and every instance
delivers what it has connections for.

```csharp
if (!string.IsNullOrWhiteSpace(redis))
{
    signalR.AddStackExchangeRedis(redis, o => o.Configuration.ChannelPrefix =
        RedisChannel.Literal("logiflow"));
}
```

A capability check, not an environment check — the same reasoning as the cache and the OTLP
exporter. And note what a backplane actually is: a **fan-out**, not a routing table. Every instance
receives every message and discards what it cannot deliver. That is fine at this size and is the
reason SignalR does not scale linearly forever.

## Try it

```bash
docker compose --profile app up -d --build
```

Two API instances share a Redis. Connect a client to `:8081`, submit an order through `:8082`, and
the push still arrives. Then take `ConnectionStrings__Redis` away from both, restart, and watch it
stop — with nothing in any log to tell you why. That silence is the lesson.

## What to remember

1. **Groups, not `Clients.All`.** And build the group name in one function, because a mismatch is
   silent on both sides.
2. **Push after commit, from the outbox.** Never from the handler — a rolled-back transaction
   cannot un-tell a browser.
3. **Translate the domain event into a DTO.** What crosses the wire is a contract with a client you
   cannot redeploy.
4. **A WebSocket handshake cannot carry a header**, so the token comes in the query string and the
   JWT handler must be told to look — narrowly.
5. **Connections are per-process.** Scaling out needs a backplane, and without one the failure is
   silent.
