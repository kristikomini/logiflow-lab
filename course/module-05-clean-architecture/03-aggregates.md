# 3. Aggregates — the transactional boundary

> Part of [Module 05 — Clean Architecture and Domain-Driven Design](README.md), section 3.
> Previous: [2. Entities vs value objects](02-entities-and-value-objects.md) ·
> Next: [4. `Result<T>` instead of exceptions](05-result-vs-exceptions.md)

---

An aggregate is a cluster of objects treated as **one unit for the purpose of changing data**. One
of them — the **aggregate root** — is the only thing the outside world is allowed to hold a
reference to. Everything else is reached through it.

That sounds like an organisational preference. It is not. It is a statement about transactions:

> **An aggregate is the largest thing you can change atomically, and the smallest thing whose
> invariants must always hold.**

Get the boundary right and concurrency mostly takes care of itself. Get it wrong and you spend a
year discovering why two users saving at the same time corrupts your data.

## The rule, and what it buys

`Order` is an aggregate root. `OrderLine` is inside it. You cannot load an `OrderLine` on its own,
you cannot save one on its own, and there is no `IOrderLineRepository`.

```csharp
public sealed class Order : AggregateRoot<OrderId>
{
    private readonly List<OrderLine> _lines = [];

    public IReadOnlyList<OrderLine> Lines => _lines;   // read-only to the outside

    public Result AddLine(Product product, int quantity)
    {
        if (!IsEditable)   return OrderErrors.NotEditable(Status);
        if (quantity < 1)  return OrderErrors.QuantityMustBePositive;
        // …and the rest of the rules, in one place
    }
}
```

**`IReadOnlyList<OrderLine>` over a private `List`, not the list itself.** If `Lines` were a
`List<OrderLine>`, any caller could write `order.Lines.Add(new OrderLine(...))` and every rule in
`AddLine` — the editable check, the quantity check, the 500-line limit, the currency check — is
bypassed silently. The read-only projection is what makes `AddLine` the *only* door.

This is the difference between a domain model and a bag of setters. If there is a second way to
change the state, the first way is not a rule, it is a suggestion.

## Where to draw the boundary

Three questions, in order:

**1. What must be true at every instant?** An order's total must equal the sum of its lines. That
consistency requirement means lines belong *inside* the order — you can never observe an order whose
total disagrees with its lines, because they change together in one transaction.

**2. What must be true only eventually?** When an order is submitted, stock must be reserved. But
"eventually, within a second" is fine — nobody is harmed by a 200 ms gap. So `StockItem` is a
**separate aggregate**, changed in its own transaction, reached through a domain event.

**3. How big does it get?** `Customer` does not contain its orders. A customer with 40 000 orders
would mean loading 40 000 rows to change a delivery address. Aggregates are loaded whole, so an
unbounded collection inside one is a performance bug you cannot fix later without re-modelling.

The result:

```
Order ──────► CustomerId          (an ID, not a Customer)
  └─ OrderLine, OrderLine, …      (inside — changes atomically with the order)

Customer                          (its own aggregate)
StockItem                         (its own aggregate)
Shipment                          (its own aggregate)
```

**Reference other aggregates by id, never by object.** `Order` holds a `CustomerId`, not a
`Customer`. If it held the object, loading an order would load a customer, then that customer's
other data, and the boundary would be meaningless. It also stops you from casually changing two
aggregates in one method and creating exactly the concurrency problem the boundary exists to
prevent.

## One transaction, one aggregate

This is the rule people find hardest, so state it plainly: **one transaction should modify exactly
one aggregate instance.**

```csharp
// ✗ Two aggregates in one transaction. Works until two users do it at once.
order.Submit();
stockItem.Reserve(quantity);
await unitOfWork.SaveChangesAsync(ct);

// ✓ Change the order. Announce it. Let stock react in its own transaction.
Result result = order.Submit();          // raises OrderSubmittedDomainEvent
await unitOfWork.SaveChangesAsync(ct);   // order + outbox row, atomically
```

Why it matters: two aggregates in one transaction means two sets of locks taken in whatever order
the code happens to run, which is a deadlock (see
[module 07 section 2](../module-07-sql-and-transactions/02-unit-of-work.md)), and it means the
transaction is held open across both. The alternative is **eventual consistency between aggregates**,
which is what [domain events](04-domain-events.md) and the
[outbox](../module-06-efcore/07-outbox-pattern.md) are for.

The honest caveat: this is a default, not a law. This repository *does* deliberately break it in
one place — inventory reservation runs inside the same transaction as the order, because an oversell
is worse than a lock. That decision is argued in
[module 07 section 4](../module-07-sql-and-transactions/05-inventory-concurrency.md), and the point
is that it is *a decision*, made once, with a reason written down.

## Computed state is not stored state

```csharp
public Money Subtotal    => _lines.Aggregate(Money.Zero(Currency), (t, l) => t + l.LineTotal);
public Money DiscountAmount => Subtotal * CustomerTier.DiscountRate();
public Money Total       => Subtotal - DiscountAmount + ShippingCost;
```

None of these are columns. They cannot drift, because there is nothing to drift *from* — the total
is derived every time it is read, from the lines that are loaded with the order anyway.

The alternative — a stored `Total` column updated by every method that touches a line — is a
consistency bug generator: one method forgets, and now you have an order whose total is wrong and no
way to know when it happened. Store what you cannot compute; compute what you can.

(The read side is allowed to disagree with this. A query that needs to *sort thousands of orders by
total* cannot afford to load every line, so it projects a total in SQL —
[module 08 section 1](../module-08-cqrs/04-read-models-and-projections.md). That is not a
contradiction: the write model owns correctness, the read model owns speed.)

## The concurrency payoff

`AggregateRoot<TId>` carries a `RowVersion`:

```csharp
public byte[]? RowVersion { get; private set; }
```

Because the aggregate is the unit of change, the version belongs on the *root* — not on each line.
Two users editing different lines of the same order is still a conflict, because the invariant they
might break (the total, the 500-line limit) spans the whole aggregate. Putting the version on the
root makes the database enforce exactly the right granularity, and nothing else. That mechanism is
[module 07 section 3](../module-07-sql-and-transactions/04-concurrency.md).

## The mistakes

**The anemic aggregate.** Public setters, and all the rules in a service that reaches in and mutates.
You have paid for the structure and got none of the protection: nothing stops the next handler from
setting `Status = Cancelled` directly. The tell is a setter.

**The god aggregate.** Everything reachable from everything, because "they're related". Related is
not the test — *changes together atomically* is the test. Most things that feel related are two
aggregates and an event.

**Repositories per entity.** `IOrderLineRepository` announces that `OrderLine` is loadable and
savable on its own, which destroys the boundary. One repository per aggregate root, and no more —
see [module 06 section 6](../module-06-efcore/05-repositories-and-uow.md).

## Try it

Open [`Order.cs`](../../src/LogiFlow.Domain/Orders/Order.cs) and try to add a line to a submitted
order from outside the class. There is no way to do it that compiles: `Lines` is read-only,
`AddLine` checks `IsEditable`, and the constructor is private. Then change `Lines` to expose the
`List` directly, and watch how many rules you can now break in one line of caller code.

## What to remember

- An aggregate is the unit of atomic change and the boundary invariants must hold across.
- Only the root is reachable from outside; collections are exposed read-only.
- Reference other aggregates by **id**, never by object reference.
- One transaction, one aggregate — and write down the reason whenever you break it.
- Bound the size: an unbounded collection inside an aggregate is a performance bug you cannot undo.
- Compute what you can derive; store only what you cannot.
- The concurrency token belongs on the root, because the root is the unit of change.

**Code:** [`Orders/Order.cs`](../../src/LogiFlow.Domain/Orders/Order.cs) ·
[`Common/AggregateRoot.cs`](../../src/LogiFlow.Domain/Common/AggregateRoot.cs)

**Next:** [4. `Result<T>` instead of exceptions](05-result-vs-exceptions.md)
