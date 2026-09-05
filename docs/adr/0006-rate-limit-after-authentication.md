# 6. Rate limiting after authentication, not before

- **Status:** Accepted — supersedes the original placement
- **Date:** 2026-09-05

## Context

The rate limiter was registered **before** `UseAuthentication`, with a comment explaining why:
*"a flood of unauthenticated requests should be cheap to reject"*. The reasoning is sound, it is
what the documentation suggests, and the pipeline-ordering notes at the top of `Program.cs`
listed the opposite arrangement as a known bug.

The limiter partitioned like this:

```csharp
string partitionKey =
    httpContext.User.Identity?.Name
    ?? httpContext.Connection.RemoteIpAddress?.ToString()
    ?? "anonymous";
```

Two things were wrong with that, and neither could be seen from the code.

**`User` is populated by `UseAuthentication`.** A limiter running before it sees an empty
`ClaimsPrincipal` on every single request. The first expression was always null.

**Nothing would have set it anyway.** `Identity.Name` comes from `ClaimTypes.Name`, and the
tokens this API issues carry `sub`, `nameidentifier`, `email` and roles — no `name`. So even
correctly placed, that expression was never going to resolve.

The result: every authenticated caller fell through to the IP. Behind a NAT, a corporate proxy
or a load balancer, that is **one budget for everybody** — precisely the self-inflicted denial of
service the comment above it claimed to prevent.

Every test passed. A test that makes ten requests never reaches a limit of a hundred. It surfaced
when `labs/Labs.LoadTests` pointed 60 distinct identities at the API and **96% of requests came
back 429** — the shape of one partition, not sixty.

## Decision

Move `app.UseRateLimiter()` **below** `UseAuthentication` and `UseAuthorization`, and partition
on `ClaimTypes.NameIdentifier` with `Identity.Name` and then the IP as fallbacks.

After the change the same run returned **2,050 requests, zero failures**.

## Alternatives considered

- **Leave it and document the limitation.** Rejected: a rate limiter that cannot tell two users
  apart is not doing the job it was added for, and the comment above it was actively misleading.
- **Two limiters** — a cheap IP one before auth, a per-user one after. The textbook answer, and
  ASP.NET Core's `UseRateLimiter` is designed to be called once; two middleware instances mean
  two option sets and two rejection paths for one concept. Deferred until there is real volumetric
  traffic to justify it.
- **Partition on `email`.** Stable per human rather than per token. Rejected because `sub` /
  `nameidentifier` is the claim that *identifies* a caller and is present on every token this API
  accepts, while email is optional in the general case.

## Consequences

- **A flood of garbage tokens now pays JWT validation before being rejected.** That is a real
  cost and it is the smaller one: a signature check on a malformed token fails in microseconds.
- Volumetric flood protection is now explicitly *not this component's job*. It belongs at the
  edge — nginx, Cloudflare, an API gateway — where it can drop packets without a process at all.
  This limiter's job is fairness between authenticated callers.
- The pipeline-ordering notes at the top of `Program.cs` were rewritten, because they taught the
  opposite of what this repository now does.
- **The general lesson, which is why this ADR exists:** the bug was invisible to a unit test, an
  integration test, a code review and the documentation. It took generating load that looked like
  real traffic. That is the argument for owning a load test, in one paragraph.
