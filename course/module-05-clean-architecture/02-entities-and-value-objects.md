# 2. Entities vs value objects

> Part of [Module 05 — Clean Architecture and Domain-Driven Design](README.md), section 2.
> Next: [3. Aggregates — the transactional boundary](03-aggregates.md)

---

Every class in your domain is one of two things, and getting it wrong is the most common modelling
mistake there is. The question that separates them is not "is it complicated" or "does it have an
id in the database". It is:

> **Does this thing have an identity that survives a change to its contents?**

Two orders placed by the same customer, for the same product, at the same second, are **two
different orders**. You can cancel one and not the other. That is an **entity**.

Two €10 notes are the same €10. There is no meaningful sense in which one is "this ten euros" and
the other is "that ten euros". That is a **value object**.

## The test that actually works

Take the object and change every field on it.

- If it is still *the same thing* — a customer who changed their name, address and email is still
  that customer — it is an **entity**, and it needs an id.
- If it is now *a different thing* — €10 with the amount changed to €20 is not "the same money,
  updated", it is different money — it is a **value object**, and it must not have an id.

The follow-up test, which catches the remaining cases: **would you ever want two of them to be
distinguishable while identical?** If yes, entity. If the idea is absurd, value object.

## Entities: identity, and the equality trap

```csharp
public abstract class Entity<TId> : IEquatable<Entity<TId>>
    where TId : notnull
{
    public TId Id { get; protected init; } = default!;

    public bool Equals(Entity<TId>? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return GetType() == other.GetType()
            && EqualityComparer<TId>.Default.Equals(Id, other.Id);
    }

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}
```

Two things in there are load-bearing.

**Equality is by id, not by contents.** An `Order` whose lines have changed is still the same
order. This is the opposite of a value object and it is the whole point of the distinction.

**`GetType() == other.GetType()`, not `other is Entity<TId>`.** This looks like pedantry and it is
not. EF Core can create a runtime-generated proxy type (`OrderProxy : Order`) for lazy loading. With
an `is` check, the proxy and the real `Order` compare **unequal** in one direction and equal in the
other, which breaks the symmetry requirement of `Equals` and produces a `HashSet` that contains the
same order twice. Including the type in `GetHashCode` is the matching half — see
[module 20](../module-20-equality-and-collections/) for what happens to a dictionary when the
contract is violated.

**A transient entity has no meaningful equality.** Two `Order`s that have not been saved both have
`Id = Guid.Empty` — or worse, both have `default` — and compare *equal* to each other. This
repository sidesteps it by generating ids in the constructor rather than letting the database do it,
which is also what makes the outbox and domain events work before `SaveChanges`.

## Value objects: immutable, and self-validating

```csharp
public readonly record struct Money(decimal Amount, Currency Currency)
{
    public Money(decimal amount, Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        Currency = currency;
        Amount = Math.Round(amount, currency.DecimalPlaces, MidpointRounding.ToEven);
    }

    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount + right.Amount, left.Currency);
    }
}
```

Four properties, and each earns its place:

**`readonly record struct`.** `record` gives structural equality for free — which is precisely the
definition of a value object, so the compiler is doing the modelling for you. `readonly` guarantees
immutability. `struct` avoids a heap allocation for something this small, and
[module 19](../module-19-memory-and-gc/) has the numbers.

**Immutable, because a mutable value object is a bug waiting for a dictionary.** Change a field on
something being used as a key and its hash changes; the entry is now in the wrong bucket and is
unreachable — present in `Count`, invisible to `TryGetValue`. Lab 04 makes you predict this before
you run it.

**It cannot be constructed in an invalid state.** The currency is checked in the constructor. A
`Money` that exists is a `Money` that is valid, everywhere, forever — which means no method
downstream has to re-check it.

**The rounding is a domain decision, in the domain.** `MidpointRounding.ToEven` — banker's rounding
— is used because always rounding `.5` up introduces a systematic upward bias across many
transactions. Putting that in the constructor means every total in the system rounds the same way,
rather than each caller deciding.

**And the currency check is the real prize.** `EnsureSameCurrency` makes adding euros to dollars
impossible rather than merely discouraged. The alternative — passing `decimal amount` and
`string currency` around separately — is *primitive obsession*, and it is how a system ends up with
a total that is the sum of two currencies and nobody notices for a quarter.

## The mistake, concretely

```csharp
// ✗ Primitive obsession. Every one of these can be got wrong at every call site.
public void AddLine(Guid productId, int quantity, decimal price, string currencyCode)

// ✗ And this compiles, and is wrong.
order.AddLine(customerId, price, quantity, "EUR");   // arguments swapped

// ✓ Value objects make the wrong call not compile.
public Result AddLine(ProductId productId, int quantity, Money unitPrice)
```

`ProductId` and `CustomerId` are both `Guid` underneath, and both are distinct types here — see
`Common/Ids.cs` and `IStronglyTypedId`. That is the same idea applied to identifiers: the type
system does the checking that a code reviewer otherwise has to.

## Where they live in this repository

| | Entities | Value objects |
|---|---|---|
| Base type | `Entity<TId>` / `AggregateRoot<TId>` | `readonly record struct` |
| Equality | by `Id` and exact type | structural, all members |
| Mutability | mutable through behaviour | immutable |
| Examples | `Order`, `Customer`, `Product`, `Shipment` | `Money`, `Address`, `EmailAddress`, `Sku`, `Weight`, `OrderNumber` |
| Persistence | a table | an owned type, or a value conversion |

That last row is [module 06 section 3](../module-06-efcore/02-value-conversions.md): a value object
has no table of its own, because it has no identity to key a table on. It is either flattened into
its owner's columns as an owned type, or converted to a single column.

## Try it

```bash
dotnet run --project labs/Labs.Playground equality
```

Then read [`Money.cs`](../../src/LogiFlow.Domain/ValueObjects/Money.cs) and try to construct an
invalid one. You cannot: there is no path through the type that produces a `Money` with a null
currency or an unrounded amount. Then try adding a `Money` in euros to one in dollars, and note that
the failure is immediate and loud rather than a wrong number.

## What to remember

- The question is identity, not complexity: does it survive a change to its contents?
- Entities compare by id **and exact type** — the type check is what survives EF Core proxies.
- Value objects are immutable, structurally equal, and validate in the constructor.
- A value object that exists is valid, so nothing downstream re-checks it.
- Primitive obsession — loose `decimal` + `string` — is the failure this prevents.
- Strongly-typed ids are the same idea applied to `Guid`.

**Code:** [`Common/Entity.cs`](../../src/LogiFlow.Domain/Common/Entity.cs) ·
[`ValueObjects/`](../../src/LogiFlow.Domain/ValueObjects/) ·
[`Common/Ids.cs`](../../src/LogiFlow.Domain/Common/Ids.cs)

**Next:** [3. Aggregates — the transactional boundary](03-aggregates.md)
