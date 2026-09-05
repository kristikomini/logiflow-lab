# 2. Minimal APIs

> Part of [Module 10 — Cross-cutting concerns](README.md), section 2.
> Previous: [1. Dependency injection and lifetimes](01-dependency-injection.md) ·
> Next: [3. Error handling](04-error-handling.md)

---

Minimal APIs are not "controllers for small projects". They are a different routing model with
measurably less overhead and no convention-based magic, and this repository uses them throughout.

```csharp
public static class OrderEndpoints
{
    public static void MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/orders")
            .WithTags("Orders")
            .RequireAuthorization();

        group.MapGet("/{id:guid}", GetById)
            .WithName("GetOrderById")
            .Produces<OrderDetailDto>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/", Create)
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem();
    }

    private static async Task<IResult> GetById(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        Result<OrderDetailDto> result = await dispatcher.SendAsync(new GetOrderByIdQuery(id), ct);
        return result.ToHttpResult();
    }
}
```

## What is actually different

**No controller base class, no `[ApiController]` conventions.** A handler is a static method taking
exactly what it needs. Nothing is inherited, so nothing is inherited by accident.

**Parameters are bound explicitly by source.** `Guid id` from the route (it appears in the template),
`IDispatcher` from DI (it is a registered service), `CancellationToken` from the framework. Where it
is ambiguous, say so: `[FromBody]`, `[FromQuery]`, `[FromServices]`. Controllers guess more, and
guessing is where over-posting bugs come from.

**Faster.** No controller activation, no action-descriptor lookup, no filter pipeline unless you add
one. Measurable at high request rates, and irrelevant below a few thousand a second — so it is a nice
property rather than the reason to choose.

**Route groups carry shared configuration.** `MapGroup` applies the prefix, the tag, the
authorization and any filters to everything inside it. That is the replacement for controller-level
attributes and it composes better, because a group can contain a group.

## Keep endpoints thin

The endpoint's whole job is **translate HTTP into a request, and a result into HTTP**:

```csharp
private static async Task<IResult> Create(CreateOrderRequest request, IDispatcher dispatcher, CancellationToken ct)
{
    Result<Guid> result = await dispatcher.SendAsync(request.ToCommand(), ct);
    return result.ToHttpResult(id => TypedResults.CreatedAtRoute(id, "GetOrderById", new { id }));
}
```

No business logic, no `DbContext`, no `if` about domain rules. Everything else — logging, validation,
transactions, caching — is a [pipeline behaviour](../module-08-cqrs/05-pipeline-behaviors.md), and
the mapping from `ErrorType` to a status code happens once in
[`ResultExtensions`](04-error-handling.md).

The test of a good endpoint: **you could delete the whole API project and the application still
works**, because nothing that matters lives there. That is also why the integration tests are worth
what they cost — they test the one layer whose job is exclusively translation.

## `TypedResults`, not `Results`

```csharp
return TypedResults.Ok(dto);          // ✓ returns Ok<OrderDetailDto> — a specific type
return Results.Ok(dto);               // returns IResult — opaque
```

`TypedResults` returns a concrete type, which means the OpenAPI document is generated from the
signature rather than from `.Produces<T>()` annotations you have to keep in step by hand, and a unit
test can assert on the result type without casting.

Where an endpoint returns more than one shape, say so in the signature:

```csharp
private static async Task<Results<Ok<OrderDetailDto>, NotFound, ProblemHttpResult>> GetById(...)
```

Verbose, and it makes the contract compiler-checked.

## Request DTOs, never entities

```csharp
public sealed record CreateOrderRequest(Guid CustomerId, IReadOnlyList<OrderLineRequest> Lines);
```

Binding the body onto a domain entity is the **over-posting** vulnerability: a caller sends
`{"total": 10, "isApproved": true}` and sets fields you never intended to expose. A request record
containing exactly the fields a client may set makes it structurally impossible —
[module 24](../module-24-security/).

It also decouples the wire format from the domain, so renaming a domain property is not a breaking
API change.

## Validation

Minimal APIs do **not** run model validation automatically the way `[ApiController]` does. That is
deliberate — the framework does less — and it means validation has to be somewhere explicit. Here it
is the `ValidationBehavior` in the pipeline, so it applies to every command whether it arrived over
HTTP or from a background worker.

The alternative, an endpoint filter, is fine for API-shaped validation:

```csharp
group.MapPost("/", Create).AddEndpointFilter<ValidationFilter<CreateOrderRequest>>();
```

Choose one. Two validation mechanisms means two places to look when something is rejected.

## Organising them

One static class per aggregate, one `MapXEndpoints` extension method, called from `Program.cs`:

```csharp
app.MapOrderEndpoints();
app.MapCatalogAndReportingEndpoints();
```

The failure mode of minimal APIs is a thousand-line `Program.cs` with every route inline. It is
avoidable in about ten minutes and it is the reason people wrongly conclude controllers scale better.

## Controllers are still fine

Do not turn this into a rule; the interview answer is the trade-off.

**Controllers win** when you rely on MVC-specific machinery — action filters, model binders, `ApiController` conventions, OData — or when the team is large and the convention is worth more than the flexibility.

**Minimal APIs win** for new services, for lower overhead, and for making dependencies explicit.

**Both are ASP.NET Core**, and they can coexist in one application. Nothing in this module is an
argument that controllers are obsolete.

## The mistakes

**Everything in `Program.cs`.** Group into extension methods from day one.

**Logic in the endpoint.** It becomes untestable without HTTP.

**Binding to a domain entity.** Over-posting.

**Forgetting `CancellationToken`.** Accept it and pass it down, or a client that disconnects leaves
your query running to completion.

**Assuming automatic validation.** It is not automatic here.

**`Results.Ok` everywhere.** Loses the typed contract and the generated OpenAPI accuracy.

## Try it

```bash
dotnet run --project src/LogiFlow.Api
```

Open `/swagger` and note that the response types are documented from the endpoint signatures. Then
change one `TypedResults.Ok(dto)` to `Results.Ok(dto)`, restart, and watch that endpoint's response
schema disappear from the document.

Then add a property to `CreateOrderRequest`, post a body containing a field that is **not** on the
record, and confirm it is ignored rather than bound — which is the over-posting defence working.

## What to remember

- Minimal APIs are a different routing model, not a smaller one.
- Parameters bind by source explicitly; be explicit where it is ambiguous.
- `MapGroup` carries prefix, tags, auth and filters for everything inside it.
- The endpoint translates HTTP to a request and a result to HTTP. Nothing else.
- `TypedResults` over `Results` — typed contracts and accurate OpenAPI.
- Bind to a request record, never to an entity.
- Validation is not automatic. Put it in the pipeline or in a filter, and pick one.
- One endpoint class per aggregate, mapped from `Program.cs`.

**Code:** [`Endpoints/OrderEndpoints.cs`](../../src/LogiFlow.Api/Endpoints/OrderEndpoints.cs) ·
[`ResultExtensions.cs`](../../src/LogiFlow.Api/Infrastructure/ResultExtensions.cs) ·
[`Program.cs`](../../src/LogiFlow.Api/Program.cs)

**Next:** [3. Error handling](04-error-handling.md)
