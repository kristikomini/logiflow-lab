# Calling someone else's API

> The outbound half of integration: typed clients, authentication that refreshes itself, where
> resilience sits in the handler pipeline and why that order is a behaviour, and receiving their
> webhooks.

Section 1 of this module established the three facts. This chapter is what they cost you in code
when the other process is an HTTP API you do not own.

There is real code to read. `src/LogiFlow.Web` is a client of `src/LogiFlow.Api` — the Blazor UI
talks to the API over HTTP like any other consumer, with a typed client, a token handler and a
token cache. It is calling *our* API rather than a third party's, but the mechanics are identical
and the file is the shortest honest example in the repository.

---

## 1. The two bugs everyone ships first

```csharp
// ✗ A client per call. HttpClient is IDisposable, so this looks right.
//   Each disposal leaves a socket in TIME_WAIT for minutes; under load the
//   ephemeral ports run out and the exception says "address in use" —
//   which reads like a server fault rather than a client bug.
using var client = new HttpClient();

// ✗ One static client forever. Fixes the sockets, and now the connection
//   never notices a DNS change, so the other side's failover is invisible
//   to you until a restart.
private static readonly HttpClient Shared = new();
```

`IHttpClientFactory` exists to make both wrong at once. It pools the expensive
`HttpMessageHandler` and rotates it periodically, so connections are reused *and* DNS is
eventually re-resolved.

📂 [`src/LogiFlow.Web/Program.cs`](../../src/LogiFlow.Web/Program.cs)

```csharp
builder.Services
    .AddHttpClient<LogiFlowApiClient>(LogiFlowApiClient.ClientName, (services, client) =>
    {
        client.BaseAddress = new Uri(GetApiBaseUrl(services));
        client.Timeout = TimeSpan.FromSeconds(30);   // ALWAYS set one
    })
    .AddHttpMessageHandler<AccessTokenHandler>();
```

**The `HttpClient` you inject is not the pooled thing.** It is a cheap, short-lived wrapper; the
handler underneath it is what is pooled. That is why a typed client is registered transient, and
why capturing the injected `HttpClient` in a static field puts you straight back into the second
bug. Module 10's captive-dependency rule, pointing outwards.

**The default timeout is 100 seconds**, which under load is not a timeout but a hang. Every
outbound client gets an explicit one.

---

## 2. Authentication that refreshes itself

A `DelegatingHandler` is middleware for outgoing requests — the same pipeline shape as module 15,
running the other way.

📂 [`src/LogiFlow.Web/Services/AccessToken.cs`](../../src/LogiFlow.Web/Services/AccessToken.cs)

```csharp
public sealed class AccessTokenHandler(IAccessTokenProvider tokenProvider) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? token = await tokenProvider.GetTokenAsync(cancellationToken);
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await base.SendAsync(request, cancellationToken);
    }
}
```

Nothing that calls the API knows a token exists. That is the point of the handler.

### The stampede, and the two lines that prevent it

The provider in that file is worth reading closely, because it gets right the thing most
first attempts get wrong:

```csharp
private readonly SemaphoreSlim _gate = new(1, 1);          // ← single-flight
private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

private bool IsValid =>
    _token is not null && DateTimeOffset.UtcNow < _expiresAt - RenewalMargin;   // ← refresh early
```

Without the gate, the moment a token expires every concurrent request discovers it simultaneously
and fires its own token request — so a burst of traffic becomes a burst against the identity
provider, which then rate-limits you. One semaphore turns fifty refreshes into one.

Without the margin you are betting that a token valid *now* is still valid when the other side
reads it, milliseconds and one network hop later. Renewing slightly early removes the bet.

Both are the same lesson in different clothes: **a cache with an expiry is a coordination problem,
not a lookup.**

---

## 3. Handler order is a behaviour, not a formality

This is the part that is hard to discover and easy to explain once seen.

Handlers form a nested pipeline in the order they are added, with the primary handler innermost.
So where the resilience handler sits *relative to the auth handler* decides what a retry actually
retries:

```
  AddHttpMessageHandler<AccessTokenHandler>()      ← outer
      AddStandardResilienceHandler()               ← inner
          primary handler → network

  Retries happen INSIDE the auth handler: the token is attached once and
  every attempt replays the same one. If the token expired mid-retry, all
  three attempts fail identically.
```

```
  AddStandardResilienceHandler()                   ← outer
      AddHttpMessageHandler<AccessTokenHandler>()  ← inner
          primary handler → network

  Retries happen OUTSIDE the auth handler: each attempt re-enters it and
  asks the provider for a token. An expired token is re-minted on attempt
  two — and a token endpoint that is down is now inside your retry budget.
```

Neither is universally correct; they encode different intentions. What is not acceptable is
choosing by accident, which is what happens when the two lines are written months apart by
different people. Decide, and put the reason in a comment next to the registration.

> A cheap belt-and-braces alternative, and what the site chapter's example does: handle **401**
> inside the auth handler itself with exactly one refresh-and-retry. Exactly once — a loop there
> is an outage of your own making.

---

## 4. Retry, and the sentence that stops it being dangerous

Section 4 of this module gave you the four ways retries go wrong. Pointed outwards, the one that
costs money is this:

**A timeout is not a failure. It is "I do not know."**

Retrying `GET` is free. Retrying `POST /orders` after a timeout is how a customer is charged
twice, because the request may well have succeeded and only the response was lost. So either the
operation is idempotent, or you must not retry it blindly:

```csharp
// A key YOU generate, stable across retries, that they use to recognise a repeat.
request.Headers.Add("Idempotency-Key", order.Id.ToString());
```

That is the same idempotency key section 3 applies to message handlers. Inbound or outbound, the
rule does not change: **at-least-once delivery plus a non-idempotent handler equals a duplicate.**

And always jitter the backoff. Without it every instance that failed during the same one-second
blip retries in unison, twice, and finishes the job the blip started.

---

## 5. Their model is not your model

```csharp
// Their DTO stays internal to Infrastructure and is mapped at the boundary.
private static TrackingResult Map(CarrierParcelDto d) => new(
    Status: d.parcel_status switch
    {
        "IN_TRANSIT" => TrackingStatus.InTransit,
        "DELIVERED"  => TrackingStatus.Delivered,
        // The arm that matters. An unknown value is THEIR new feature.
        // Throwing turns their routine release into your outage; guessing
        // corrupts data quietly. Unknown is a real answer.
        _            => TrackingStatus.Unknown
    },
    Eta: DateTime.TryParse(d.eta, CultureInfo.InvariantCulture,
                           DateTimeStyles.AssumeUniversal, out var t) ? t : null);
```

Two things in that snippet are load-bearing.

The **unknown arm** exists only because there is one place to put it. Let their DTO into the domain
and their next enum value breaks you in twenty places instead of one — the anti-corruption argument
from module 05, applied to a third party.

The **explicit `InvariantCulture`** is not decoration. Module 23 explains why an Italian machine
reads `1234.5` as `12345`; an integration boundary is exactly where that stops being a demo. Their
`03/04/2026` is the fourth of March or the third of April depending on who wrote it, and nobody
will tell you which.

---

## 6. Receiving: the webhook endpoint

The inbound half is a public, unauthenticated URL that causes writes in your database, which makes
it the most security-sensitive endpoint you will write. Four rules, and the first is the one that
costs a day:

1. **Verify the signature against the raw body.** Deserialising and re-serialising changes the
   bytes — whitespace, key order, number formatting — and the HMAC will never match again.
   `EnableBuffering()` and read the stream.
2. **Compare in constant time.** `CryptographicOperations.FixedTimeEquals`, not `SequenceEqual`.
3. **Reject stale timestamps**, so a captured request cannot be replayed indefinitely.
4. **Acknowledge fast and queue the work.** Process inline and they will time out and redeliver —
   which is how one event becomes four.

Then deduplicate on their event id, because every webhook provider is at-least-once and the second
delivery is byte-identical to the first. It is section 3 again, with someone else's retry policy
driving it.

---

## 7. Testing it without them

```csharp
// No package, no network, works anywhere.
public sealed class StubHandler(HttpStatusCode code, string body) : HttpMessageHandler
{
    public List<HttpRequestMessage> Seen { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage req, CancellationToken ct)
    {
        Seen.Add(req);
        return Task.FromResult(new HttpResponseMessage(code)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
    }
}
```

The tests worth writing are the unhappy ones — an unknown status value, a timeout, malformed JSON,
retries stopping at three. The happy path was never the risk.

For behaviour that is genuinely HTTP-level, like proving a circuit breaker opens, you need a real
server that can be programmed with failures and delays: **WireMock.Net**. And keep one recorded
real payload per endpoint as a fixture, so a silent change on their side arrives as a failing test
rather than a production surprise.

---

## Golden rules

1. **Never `new HttpClient()` per call, never one static forever.** Register a typed client and let
   the factory rotate the handler.
2. **Set an explicit timeout on every outbound client.** The 100-second default is a hang.
3. **A cache with an expiry is a coordination problem.** Single-flight the refresh with a
   semaphore, and renew before expiry rather than at it.
4. **Handler order decides what a retry retries.** Resilience outside auth re-mints the token;
   inside auth it replays the old one. Choose deliberately and write down which.
5. **A timeout means unknown.** Retry only what is idempotent, or send a key they can recognise.
6. **Map their DTO at the boundary and give the unknown value an arm.** Throwing turns their
   release into your outage.
7. **Parse their dates and numbers with an explicit culture**, never the server's default.
8. **Verify a webhook against the raw body, in constant time**, reject stale timestamps, and answer
   before doing the work.
9. **Assume every event arrives twice.** Deduplicate on their id.
10. **Test the failures.** The happy path was never the risk.

---

← [Sagas and eventual consistency](05-sagas-and-eventual-consistency.md) · [Module 25](README.md)
