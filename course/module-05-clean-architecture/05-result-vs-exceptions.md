# 4. `Result<T>` instead of exceptions

> Part of [Module 05 — Clean Architecture and Domain-Driven Design](README.md), section 4.
> Previous: [3. Aggregates](03-aggregates.md) · Next: [5. Domain events](04-domain-events.md)

---

An exception means *this should not have happened*. A customer submitting an empty order **should**
happen — it is Tuesday, and someone clicked the button early. Modelling an ordinary business outcome
as an exception is a category error, and it costs you three things: performance, honesty about the
API, and the ability to collect more than one failure.

The rule this repository uses:

> **If a human could reasonably cause it, return a `Result`. If only a bug or the infrastructure
> could cause it, throw.**

## The type

```csharp
public class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    public static implicit operator Result(Error error) => Failure(error);
}

public sealed class Result<TValue> : Result
{
    public TValue Value { get; }                 // throws if you read it on a failure
    public bool TryGetValue([NotNullWhen(true)] out TValue? value);

    public static implicit operator Result<TValue>(TValue value) => Success(value);
    public static implicit operator Result<TValue>(Error error)  => Failure<TValue>(error);
}
```

**The two implicit conversions are what make it pleasant to write.** Without them every return
statement is `Result<Order>.Failure(OrderErrors.EmptyOrder)`. With them:

```csharp
public Result AddLine(Product product, int quantity)
{
    if (!IsEditable)  return OrderErrors.NotEditable(Status);   // Error → Result
    if (quantity < 1) return OrderErrors.InvalidQuantity;
    ...
    return Result.Success();
}

public static Result<Order> CreateDraft(...)
{
    if (customerId.Value == Guid.Empty) return OrderErrors.CustomerRequired;   // Error → Result<Order>
    return new Order(...);                                                     // Order → Result<Order>
}
```

That reads almost exactly like the version that throws, which matters: a discipline that is annoying
to follow does not get followed.

**`Value` throws if you read it on a failure**, deliberately. A `Result` that silently returns
`default` on failure is worse than an exception, because now the wrong answer travels. If you want
to branch, use `TryGetValue` — and note the `[NotNullWhen(true)]`, which teaches the nullable
analyser that a `true` return means the value is non-null, so the compiler stops warning you.

## Errors are values, not strings

```csharp
public record Error(string Code, string Description, ErrorType Type);

public enum ErrorType { Failure, Validation, NotFound, Conflict, Unauthorized, Forbidden }
```

And they are declared once, per aggregate, in a static class:

```csharp
public static class OrderErrors
{
    public static readonly Error EmptyOrder =
        Error.Validation("Order.Empty", "Cannot submit an order with no lines.");

    public static Error NotFound(OrderId id) =>
        Error.NotFound("Order.NotFound", $"No order found with id '{id}'.");
}
```

Three consequences worth noticing:

**The code is stable and the description is not.** `Order.Empty` is what a client checks and what a
log query groups by; the sentence is what a human reads and what a translator changes. Mixing them —
`return "Cannot submit an empty order"` — means any wording change is a breaking API change.

**`ErrorType` is what maps to HTTP.** The API layer does the translation *once*, in
`ResultExtensions`, rather than every endpoint deciding: `Validation` → 400, `NotFound` → 404,
`Conflict` → 409, `Forbidden` → 403. The domain never mentions HTTP, and the mapping lives at the
boundary where HTTP is a legitimate concern —
[module 10 section 3](../module-10-cross-cutting/04-error-handling.md).

**Errors are testable by identity.** `result.Error.ShouldBe(OrderErrors.EmptyOrder)` is exact.
Asserting on a message is asserting on prose.

## Why not exceptions for this

**Cost.** Throwing is expensive — a stack walk, and on .NET it is orders of magnitude slower than a
return. In a validation-heavy path that runs on every request, this is measurable rather than
theoretical.

**Honesty.** A method signature that returns `Result<Order>` tells the caller failure is expected and
must be handled. `Order CreateDraft(...)` claims it always succeeds and lies. C# has no checked
exceptions, so the type is the only place this can be said.

**Composition.** Several failures can be collected. Exceptions give you the first one and stop:

```csharp
Result validation = Result.FirstFailureOrSuccess(
    ValidateCustomer(command),
    ValidateLines(command),
    ValidateAddress(command));

if (validation.IsFailure) return validation.Error;
```

**Control flow that reads as control flow.** `if (result.IsFailure)` is visible on the page.
`try/catch` for an expected outcome hides the ordinary path inside an exceptional-looking construct.

## When you should still throw

Do not turn this into a religion — the interview question is usually about the boundary, not the
mechanism:

- **Programmer error.** A null argument, an invalid enum, a broken invariant. `ArgumentNullException`
  is right; the caller has a bug and should crash in development. Note that `Order.CreateDraft`
  *does* `ArgumentNullException.ThrowIfNull(currency)` while *returning* a `Result` for the empty
  customer id — different kinds of wrong.
- **Infrastructure failure.** The database is unreachable, the disk is full. Nobody's business rule
  is involved and no caller can sensibly recover at that point.
- **Truly exceptional states** you cannot continue from.

The global exception handler catches those, logs them with a trace id, and returns Problem Details —
so the user still gets a civil answer, and the log gets the stack trace.

## The mistakes

**Returning `Result` and then ignoring it.** `order.AddLine(product, 5);` compiles and discards the
failure. This is the one real weakness compared to exceptions. Mitigation: check every call in review,
and lean on the fact that most calls immediately need the value anyway.

**Putting the exception inside the Result.** `Result.Failure(new Exception(...))` gets you the cost
of both and the benefits of neither.

**Using it for everything, including `null` reference bugs.** If the answer to a failure is "fix the
code", it is not a `Result`.

## Try it

Open [`Order.cs`](../../src/LogiFlow.Domain/Orders/Order.cs) and count the `return OrderErrors.…`
lines. Every one is a rule a user can trip over, expressed as a value. Then look at
[`ResultExtensions.cs`](../../src/LogiFlow.Api/Infrastructure/ResultExtensions.cs) — the entire
domain-to-HTTP translation is one small file, because `ErrorType` did the classifying already.

## What to remember

- Expected failure is a return value; unexpected failure is an exception.
- The test: could a reasonable human cause this? Then it is a `Result`.
- Implicit conversions from `TValue` and `Error` are what make the discipline bearable.
- Error **codes** are the contract; descriptions are prose and may change.
- `ErrorType` maps to HTTP once, at the boundary — the domain never mentions status codes.
- Still throw for programmer error and infrastructure failure.
- The weakness is a discarded `Result`. Watch for it in review.

**Code:** [`Results/Result.cs`](../../src/LogiFlow.Domain/Results/Result.cs) ·
[`Results/Error.cs`](../../src/LogiFlow.Domain/Results/Error.cs) ·
[`Orders/OrderErrors.cs`](../../src/LogiFlow.Domain/Orders/OrderErrors.cs)

**Next:** [5. Domain events](04-domain-events.md)
