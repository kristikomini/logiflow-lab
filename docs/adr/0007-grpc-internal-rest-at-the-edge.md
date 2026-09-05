# 7. gRPC between services, REST at the edge

- **Status:** Accepted
- **Date:** 2026-09-05

## Context

Two gaps, both of which the Emilia-Romagna market asks about and neither of which this repository
had:

**Real-time.** The warehouse screen finds out an order shipped by polling, or not at all. SignalR
appeared in the codebase only as the transport under Blazor Server's circuit — there was no hub
and nothing pushed.

**gRPC.** Mentioned once in the course, implemented nowhere. "When would you use gRPC instead of
REST?" is a standard interview question, and the answer that gets marked down is "it's faster".

The risk in adding either is scope: both are easy to bolt on in a way that duplicates the
existing API and then drifts from it.

## Decision

**gRPC for internal, service-to-service traffic; REST stays the public edge.** One `.proto`, one
service (`OrderLookup`), sharing the *same query handlers* as the REST endpoints — the transport
differs, the behaviour must not, and `GrpcOrderLookupTests` asserts the two surfaces agree.

**SignalR for real-time, fed from the outbox**, never from a command handler. The push goes
through `IIntegrationEventPublisher` ([ADR 4](0004-transactional-outbox.md)), whose SignalR
implementation lives in the API project — the only project allowed to know SignalR exists.
`Infrastructure` calls the interface and never learns what it got.

The reason for gRPC, stated in the order that matters: **the contract is compiled, not
documented.** Speed is a side effect.

## Alternatives considered

- **gRPC for everything, including the edge.** A browser cannot call it without a proxy, it is
  unreadable in a log, and every consumer needs the toolchain. Choosing it because it benchmarks
  well is the mistake this decision exists to avoid.
- **REST for everything, including internally.** Where the repository already was. Nothing forces
  the contract, and a field the server stops sending becomes a `null` in a client three weeks
  later.
- **Pushing domain events straight down the SignalR connection.** Would put `Money`, `Weight` and
  a customer's `Address` into a browser that asked for a status string, and would make every
  future rename of a domain record a breaking change for a client you cannot redeploy.
  `OrderStatusUpdate` is the translation, written by hand on purpose.
- **Server-Sent Events instead of SignalR.** Lighter and one-directional, which is all this needs.
  Rejected because SignalR brings reconnection, transport fallback and group addressing, and
  because it is what the local market names.

## Consequences

- **gRPC needs HTTP/2 end to end.** Kestrel negotiates it over TLS via ALPN; over plain HTTP it
  must be told, so `appsettings.json` now sets `Kestrel:EndpointDefaults:Protocols`. Without it,
  gRPC fails over `http://` and works over `https://` — a genuinely confusing pair of symptoms.
- The `.proto` is generated with `GrpcServices="Both"`, so the API ships a client it does not
  call. The alternative is a `LogiFlow.Contracts` project, which is what you do the moment a
  second solution consumes this. Deliberately deferred.
- **SignalR connections are per-process.** Two instances need the Redis backplane, wired as a
  capability check rather than an environment check. Without one, an event handled by instance A
  reaches nobody on instance B, silently.
- **A WebSocket handshake cannot carry an `Authorization` header**, so the JWT handler now reads
  `access_token` from the query string — scoped to `/hubs` only, because a token in a URL is a
  token in the access log.
- Two more packages in the graph (`Grpc.AspNetCore`,
  `Microsoft.AspNetCore.SignalR.StackExchangeRedis`) and a build-time `protoc` step.
- proto3 has no decimal, so money crosses the wire as a **string**. `GrpcOrderLookupTests`
  asserts that round trip exactly, so "simplifying" it to a `double` fails a test rather than a
  reconciliation.
