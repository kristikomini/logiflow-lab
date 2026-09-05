# 4. Pipeline behaviours

> Part of [Module 08 — CQRS and the mediator pattern](README.md), section 4.
> Previous: [3. Building a mediator](03-building-a-mediator.md) ·
> Next: [Read models and projections](04-read-models-and-projections.md)

---

Once every operation goes through one dispatcher, you can wrap **all of them at once**. That is the
real reason to have a mediator: logging, validation, transactions and caching stop being a line in
two hundred handlers and become one class each.

```csharp
public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct);
}
```

It is the [Decorator pattern](../module-26-patterns-and-solid/) — same shape in, same shape out, so
they stack — and it is the same idea as ASP.NET Core middleware, one layer further in.

## The stack

```
┌─ LoggingBehavior ─ start, duration, outcome ──────────────────────┐
│ ┌─ ValidationBehavior ─ reject bad input before anything runs ───┐│
│ │ ┌─ CachingBehavior ─ queries only; return early on a hit ─────┐││
│ │ │ ┌─ TransactionBehavior ─ commands only ──────────────────┐  │││
│ │ │ │   CreateOrderCommandHandler                            │  │││
│ │ │ └────────────────────────────────────────────────────────┘  │││
│ │ └─────────────────────────────────────────────────────────────┘││
│ └────────────────────────────────────────────────────────────────┘│
└───────────────────────────────────────────────────────────────────┘
```

**Order is behaviour, not preference.** Read it outside-in and every position is an argument:

- **Logging outermost**, so it sees everything including validation failures and cache hits. Move it
  inward and your logs stop reporting the requests that failed fastest.
- **Validation before the transaction**, so an invalid request never opens one. Reversed, every bad
  request costs a `BEGIN TRAN` and a `ROLLBACK`.
- **Caching before the transaction**, so a cache hit does no database work at all. That is the entire
  point of the cache.
- **Transaction innermost**, so it is held for the shortest possible time — the rule from
  [module 07 section 2](../module-07-sql-and-transactions/02-unit-of-work.md).

## How the nesting is built

```csharp
Task<TResponse> Next() => handler.HandleAsync((TRequest)request, ct);

return sp.GetServices<IPipelineBehavior<TRequest, TResponse>>()
         .Reverse()
         .Aggregate((RequestHandlerDelegate<TResponse>)Next,
                    (next, behavior) => () => behavior.HandleAsync((TRequest)request, next, ct))();
```

Start with the handler as the innermost delegate. Fold the behaviours in **reverse registration
order**, each capturing the delegate built so far as its `next`. The result is one delegate that, when
invoked, runs the whole nest.

`.Reverse()` is what makes registration order equal execution order. Drop it and your logging ends up
innermost, which is exactly the wrong place — and the bug is silent, because everything still works.

## The four

### Logging — outermost

```csharp
using (logger.BeginScope(new Dictionary<string, object>
{
    ["CorrelationId"] = Activity.Current?.TraceId.ToString() ?? traceId,
    ["RequestName"]   = typeof(TRequest).Name
}))
{
    logger.LogInformation("Handling {RequestName}", name);
    TResponse response = await next();
    logger.LogInformation("Handled {RequestName} in {Ms}ms", name, sw.ElapsedMilliseconds);
    return response;
}
```

**Message templates with named placeholders**, never string interpolation — the difference between a
queryable field and an opaque string, and the reason analyser rule CA2254 exists.

**The scope is the other half.** `BeginScope` attaches the correlation id to every log line written
anywhere inside the handler, including deep in EF Core. When a customer reports a failure at 14:32
you filter on one id and see the whole request. That is
[module 11](../module-11-observability/) working because of one `using` here.

Slow requests are logged at `Warning` above a threshold, which turns "the app feels slow" into a
query.

### Validation — before the transaction

```csharp
IValidator<TRequest>[] validators = sp.GetServices<IValidator<TRequest>>().ToArray();
if (validators.Length == 0) return await next();

ValidationResult[] results = await Task.WhenAll(validators.Select(v => v.ValidateAsync(request, ct)));
ValidationFailure[] failures = results.SelectMany(r => r.Errors).Where(f => f is not null).ToArray();

if (failures.Length > 0)
    return CreateValidationResult<TResponse>(failures);
```

**Input validation, not business rules.** "Quantity must be a positive integer" is a validator.
"There is not enough stock" is the domain's job, because it needs state and cannot be decided from
the request alone. Putting business rules in validators is how logic leaks out of the aggregate and
back into services.

**Collect every failure, not the first.** A form that reports one error at a time is a form that
takes six round trips to submit.

**It returns a failed `Result`, it does not throw.** Consistent with
[module 05 section 4](../module-05-clean-architecture/05-result-vs-exceptions.md): a user typing
something wrong is not exceptional.

### Caching — queries only

```csharp
if (request is not IQuery<TResponse>) return await next();
```

One line, and caching a command becomes impossible. This is the payoff for the
[marker interfaces](01-what-cqrs-actually-is.md) — the pipeline reads intent from the type system
rather than from a naming convention or an attribute somebody forgets.

The invalidation, TTL and stampede problems are [module 10](../module-10-cross-cutting/); the point
here is only that the *decision* is structural.

### Transaction — commands only

```csharp
if (request is not ICommand<TResponse>) return await next();

return await unitOfWork.ExecuteInTransactionAsync(async token => await next(), ct);
```

Queries get no transaction, deliberately: a read does not need one and would only take locks. And
because this wraps every command, no handler contains `BeginTransaction` — the boundary is declared
once, in the place that knows what a command is.

## Why not a base class

The obvious alternative is `abstract class BaseHandler<T>` with `OnBefore`/`OnAfter`. It fails for
reasons worth being able to state:

- **Behaviours compose; inheritance does not.** Four concerns is four classes stacked in a chosen
  order. With inheritance it is one base class doing four things, in an order you cannot change per
  request type.
- **Order becomes implicit.** Above it is a registration list you can read.
- **You cannot opt out per concern.** Here, a behaviour skips itself with one `if`.
- **Testing.** Each behaviour is unit-testable with a fake `next`. A base class is testable only
  through a concrete handler.

## The costs

**Stack traces get deep.** Four wrappers plus the dispatcher's own wrapper before you reach the
handler. Real, and the price of the decoupling.

**Everything runs for everything.** A behaviour that does expensive work unconditionally taxes every
request. Guard clauses first, work second — which is why each of the four above starts with an `if`.

**Order is invisible at the call site.** The pipeline is configured far from the handler, so a
surprising interaction is diagnosed in the registration, not where it appears.

## Try it

Set a breakpoint in each behaviour and submit an order. Watch the order they are *entered* in
(outside-in) and the reverse order they are *exited* in. Then delete `CachingBehavior` from the
registration and run the tests: nothing fails to compile, no handler changes, and the behaviour is
simply gone. That is what loose coupling buys, measured in edits.

Then swap `ValidationBehavior` and `TransactionBehavior` in the registration and watch a transaction
open for a request that was always going to be rejected.

## What to remember

- One dispatcher means every operation can be wrapped at once.
- A behaviour is a decorator: same shape in and out, so they stack.
- Order is behaviour — logging outermost, transaction innermost, validation and caching before it.
- `.Reverse()` before folding is what makes registration order equal execution order.
- Message templates, not interpolation; `BeginScope` for the correlation id.
- Validators check input shape; the aggregate checks business rules.
- Marker interfaces let behaviours opt in or out structurally, in one line.
- Costs: deeper stack traces and a pipeline that runs for everything. Guard first.

**Code:** [`Behaviors/`](../../src/LogiFlow.Application/Behaviors/) ·
[`Dispatcher.cs`](../../src/LogiFlow.Application/Abstractions/Messaging/Dispatcher.cs)

**Next:** [Read models and projections](04-read-models-and-projections.md)
