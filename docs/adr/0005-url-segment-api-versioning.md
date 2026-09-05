# 5. Version in the URL, with a rewrite for the pre-versioning paths

- **Status:** Accepted
- **Date:** 2026-09-05

## Context

The API had twenty-two endpoints and no versioning at all — not a mechanism that had been
considered and rejected, simply an absence. That is survivable right up until the first response
shape has to change, at which point there is no move that does not break somebody.

The specific change that forced it: `GET /api/reports/sales` returns a **bare JSON array**.
There is nowhere in that shape to put a total, a currency, or paging metadata, and the first
request for any of them means a new top-level type — which breaks every client doing
`response.map(...)` on the first byte.

Two constraints made this harder than a greenfield decision. `/api/orders` and friends are
already published, and the integration tests, `requests/logiflow.http` and the Blazor UI all use
them. And the Academy service, the Web UI and the tests all had to keep working through the
change.

## Decision

**URL-segment versioning** — `/api/v1/...`, `/api/v2/...` — with the pre-versioning paths kept
alive by an **internal rewrite** onto v1, and RFC 8594 `Deprecation` / `Sunset` headers on every
rewritten response.

v2 contains **one endpoint**, not a copy of the API. Anything a caller does not find on v2 it
finds on v1.

Implemented by hand in `Api/Infrastructure/ApiVersioning.cs` (~40 lines) rather than with
`Asp.Versioning.Http`, on the same reasoning as [ADR 3](0003-hand-written-mediator.md).

## Alternatives considered

- **A version header** (`X-Api-Version: 2`). Clean URLs and easy defaulting, and invisible: a
  caller on the wrong version debugs by guessing, and a shared cache needs `Vary` set correctly
  or it serves one version's body to the other version's client.
- **Media-type versioning** (`Accept: application/vnd.logiflow.v2+json`). The most correct by
  REST's own reasoning and the least convenient in every client library, which is why it is rare
  outside GitHub.
- **308 redirects instead of a rewrite** for the old paths. More honest — the client learns the
  new URL — and it costs a round trip on every call and moves the risk onto the client's HTTP
  stack, since `308` replays method and body only if the client implements it correctly and the
  body is replayable. A large upload over a slow link discovers this as an intermittent failure.
- **Duplicating all 22 endpoints into v2.** The reason teams postpone versioning until the change
  has become a rewrite.

## Consequences

- The rewrite middleware **must run before `UseRouting`**, so `Program.cs` now calls
  `app.UseRouting()` explicitly. `WebApplication` otherwise inserts it at the very top of the
  pipeline and the rewrite changes a path routing has already finished with. The first
  implementation did exactly that and 404'd every legacy URL with nothing in any log.
- Old callers get no round trip and no error, but also no *signal* beyond the headers. If they
  never read `Sunset`, the January 2027 removal will surprise them. Redirects would fix that and
  cost more; both, in sequence, is how a large API actually migrates.
- Two shapes of the same report now exist and both must keep working. `ApiVersioningTests`
  asserts that, including that the error contract stays shared across versions.
- The `Sunset` date is a promise. Extending it is survivable; having no date makes the old shape
  permanent.
