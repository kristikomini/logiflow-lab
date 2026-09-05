# 2. Versioning an API you have already published

> Part of [Module 15 — ASP.NET Core in depth](README.md).

---

Every REST interview asks it, and most codebases answer with a shrug: **how do you change a
response that somebody is already consuming?**

You cannot. Not in place. A client parsing your JSON is coupled to its shape, and the only thing
standing between "we added a field" and "we took production down" is whether that client's
deserializer happens to be lenient. So the shape gets a version, and the old shape keeps working
until the last caller has moved off it.

## First: what is actually breaking?

Not every change needs a version, and treating them all as if they do is how teams end up on v7.

| Change | Breaking? |
| --- | --- |
| Adding a field to a response | **No** — for any sane client. This is why you can ship most changes without ceremony. |
| Adding an optional request field | **No** |
| Renaming a field | **Yes** |
| Removing a field | **Yes** |
| Changing a type (`string` → `number`) | **Yes**, and the worst kind: it often parses |
| Changing the top-level shape | **Yes** |
| Making an optional request field required | **Yes** |
| Changing what an endpoint *means* | **Yes**, and no version number will save you |

The last row is the one nobody catches. If `total` silently starts including VAT, every client
still parses it perfectly and every invoice is wrong.

## Three places to put the version

**In the URL** — `/api/v2/reports/sales`. Visible in a log, in a browser, in a curl command
somebody pastes into a ticket. Cacheable per version by any intermediary with no configuration,
because the URL *is* the cache key. Purists object that the resource has not changed, only its
representation, so the version does not belong in the identifier — which is correct, and loses to
the fact that everyone can see it.

**In a header** — `X-Api-Version: 2`. Clean URLs, easy defaulting. Also invisible: a caller on the
wrong version debugs by guessing, and a shared cache needs `Vary` set correctly or it will hand one
version's body to the other version's client.

**In the media type** — `Accept: application/vnd.logiflow.v2+json`. The most correct by REST's own
reasoning, since it is content negotiation doing exactly what it is for. Also the one no client
library makes convenient, which is why it is rare outside GitHub.

This codebase uses the URL. Say all three in an interview, then say which you would pick and why.

## The mechanics, in forty lines

`ApiVersioning.MapApiVersion` creates one route group per version, so the version is a property of
the group rather than something twenty-two route strings have to agree about:

```csharp
RouteGroupBuilder v1 = app.MapApiVersion("v1");
v1.MapOrderEndpoints();
v1.MapProductEndpoints();
// …

RouteGroupBuilder v2 = app.MapApiVersion("v2");
v2.MapReportingEndpointsV2();
```

**v2 is one endpoint, not a second copy of the API.** Anything a caller does not find on v2 it
finds on v1. That is what makes a version bump cheap enough to actually do — the alternative,
duplicating twenty-two endpoints to change one, is why teams put versioning off until the change
has become a rewrite.

Every versioned response also carries the full set:

```
api-supported-versions: v1, v2
```

so a client hard-coded to v1 can discover that v2 exists without reading documentation nobody sent
it.

> **The package.** `Asp.Versioning.Http` (formerly `Microsoft.AspNetCore.Mvc.Versioning`) does all
> of this, plus reading the version from any of the three sources at once, plus an OpenAPI document
> per version. It is the right answer on a team and the right name to say in an interview. It is
> hand-written here for the same reason the mediator is: forty lines you can read beat a package
> you cannot, when the point is understanding what the package does.

## The interesting half: the URLs that existed before versions did

`/api/orders` was published before any of this. Those callers are real and cannot be redeployed on
your schedule. Two options:

**308 Permanent Redirect** is the honest one — the client learns the new URL and a well-behaved one
updates its own records. It also costs a round trip on every call, and it moves the risk onto the
client's HTTP stack: `308` preserves method and body *only* if the client implements it correctly
and *only* if the body can be replayed, which a non-buffered upload stream cannot. A caller posting
a large payload over a slow link discovers this as an intermittent failure.

**An internal rewrite** is invisible and free. The path is changed before routing runs, so exactly
one set of endpoints exists and there is no duplicate registration to keep in step:

```csharp
public static IApplicationBuilder UseUnversionedApiCompatibility(this IApplicationBuilder app) =>
    app.Use(async (context, next) =>
    {
        if (TryRewrite(context.Request.Path, out PathString rewritten))
        {
            context.Request.Path = rewritten;
            context.Response.Headers["Deprecation"] = "true";
            context.Response.Headers["Sunset"] = UnversionedSunset.ToString("R", CultureInfo.InvariantCulture);
        }

        await next(context);
    });
```

What it gives up is the client's chance to learn — hence the headers. **Redirect when you want
callers to move; rewrite when you want them not to break.** Both, in sequence, is how a large API
actually migrates.

### Announce the removal in a way a machine can read

RFC 8594 defines `Sunset` precisely so a removal can be acted on programmatically, and so "we told
you" becomes a checkable claim rather than a mailing list nobody read:

```
Deprecation: true
Sunset: Fri, 01 Jan 2027 00:00:00 GMT
Link: <https://api.example.com/api/v1/orders>; rel="successor-version"
```

A **date**, not "soon". Publishing a date you later extend is survivable. Publishing no date makes
the old shape permanent.

## The trap: this middleware must run before routing

The first version of this feature 404'd every legacy URL. Nothing threw; the middleware ran and did
exactly what it was told.

`WebApplication` inserts `UseRouting` at the **top** of the pipeline automatically — before the
first middleware you register — unless you call it yourself, in which case yours is used where you
put it. Routing selects the endpoint by matching `Request.Path`, so a rewrite running after it
changes a path nobody will look at again.

```csharp
app.UseUnversionedApiCompatibility();
app.UseRouting();               // explicit, and BELOW the rewrite
```

The Academy service has the same line for the same reason, guarding static files instead. Two
unrelated services, one trap — and both of them found it the same way, by a URL that returned 404
while everything else worked.

## The change worth having an opinion about

`/api/v1/reports/sales` returns a bare JSON array:

```json
[ { "periodLabel": "2026-01", "totalRevenue": 48210.00 }, … ]
```

It is the obvious thing to write and it is a dead end, for a reason that only appears later: **there
is nowhere to put anything else.** The day somebody asks for the total across the window, or for
paging because a two-year daily report is 730 objects, or for the currency, the answer is a new
top-level shape — and a client doing `response.map(…)` breaks on the first byte.

v2 wraps it:

```json
{ "data": [ … ], "from": "…", "to": "…", "period": 2, "count": 12,
  "totalRevenue": 481203.00, "currency": "EUR" }
```

Now every future addition is a new property on an object, which is the one change a JSON consumer
tolerates by default.

**Never return a bare array from an endpoint you intend to keep.** (There is a second, historical
reason: a top-level JSON array was once executable as JavaScript, which made it a cross-site
data-leak vector. Browsers closed that; the design argument was correct on its own and outlived the
exploit.)

Note what did *not* change: the query, the handler, the domain. This is entirely a presentation
concern, which is why it lives in the API layer and reshapes what the dispatcher already returned.
**If a version bump forces a change in Application or Domain, the version is not what changed — the
requirement is.**

And the error contract stays shared. A validation failure looks identical on both versions; a
version that also changes how errors are reported is two changes wearing one coat.

## Try it

```bash
curl -i -H "Authorization: Bearer $TOKEN" http://localhost:5199/api/orders
```

```
HTTP/1.1 200 OK
Deprecation: true
Sunset: Fri, 01 Jan 2027 00:00:00 GMT
Link: <http://localhost:5199/api/v1/orders>; rel="successor-version"
api-supported-versions: v1, v2
```

Then compare the two report shapes:

```bash
curl -s -H "Authorization: Bearer $TOKEN" \
  "http://localhost:5199/api/v1/reports/sales?from=2025-01-01T00:00:00Z&to=2026-01-01T00:00:00Z&period=Monthly"
curl -s -H "Authorization: Bearer $TOKEN" \
  "http://localhost:5199/api/v2/reports/sales?from=2025-01-01T00:00:00Z&to=2026-01-01T00:00:00Z&period=Monthly"
```

## What to remember

1. **Adding a field is not breaking. Renaming, removing and retyping are.** Version for the second
   group, not the first.
2. **URL, header, or media type.** Know all three; URL wins on visibility.
3. **A new version is the endpoints that changed**, not a copy of the API.
4. **Rewrite so old callers keep working; `Deprecation` and `Sunset` so they know to move.** With a
   date.
5. **The rewrite must run before `UseRouting`**, and `WebApplication` puts `UseRouting` first
   unless you call it yourself.
6. **Never publish a bare array.** There is nowhere to put the next field.
