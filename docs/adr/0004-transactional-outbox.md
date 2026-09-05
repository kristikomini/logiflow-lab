# 4. A transactional outbox for anything crossing a process boundary

- **Status:** Accepted
- **Date:** 2026-08-20

## Context

An order is submitted. Two things must happen: the row is written, and the world is told. They
live in different systems, so no transaction covers both — and every ordering has a failure mode:

- **Send first, then commit.** The commit fails and you have announced an order that does not
  exist. Unrecoverable — you cannot un-send.
- **Commit first, then send.** The process dies in between: the order exists and nobody was
  told. Silent, and it is the one that happens in production.

This is the dual-write problem. It has no clever solution, only a structural one.

## Decision

Domain events are written to an **outbox table in the same transaction as the data**. A
background worker (`OutboxProcessor`) polls the table, publishes each message through
`IIntegrationEventPublisher`, and marks it processed.

The message is committed atomically with the change that produced it, and delivery becomes a
separate, retryable problem.

## Alternatives considered

- **Publish from the handler.** What most codebases do. Both failure modes above are live.
- **Two-phase commit** across database and broker. It exists; it holds locks across a network,
  blocks everyone when the coordinator dies, and is unsupported by most cloud databases and
  brokers.
- **Change data capture** (Debezium and friends). Genuinely excellent and the right answer at
  scale. It needs infrastructure this repository deliberately does not require, and it couples
  your integration contract to your table schema.

## Consequences

- Delivery is **at-least-once**, never exactly-once. Consumers must be idempotent, and that is
  stated wherever a consumer is written rather than hoped for.
- There is a lag — the poll interval — between commit and publish. `OrderStatusUpdate` therefore
  carries the event's own timestamp rather than the send time: after an outage those are minutes
  apart, and showing one as the other is a lie told exactly when someone is looking closely.
- The outbox table needs a retention policy or it grows forever.
- A row that fails five times is quarantined rather than retried indefinitely, so one poison
  message cannot stall everything behind it.
- `IIntegrationEventPublisher` is a log line by default and a SignalR push when the API is
  running. Swapping in RabbitMQ or Service Bus replaces one class — the claim the abstraction
  exists to make, and [ADR 7](0007-grpc-internal-rest-at-the-edge.md) is where it is used.
