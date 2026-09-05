# 4. Inventory: where oversells come from

> Part of [Module 07 — SQL, transactions and concurrency](README.md), section 4.
> Previous: [3. Optimistic concurrency](04-concurrency.md) ·
> Next: [6. Pagination that does not lie](06-pagination.md)

---

Selling something you do not have is the most expensive bug in commerce software: you have taken
money, made a promise, and the recovery is a human apologising. It is also almost always the same two
bugs — **one number instead of three**, and **check-then-act instead of an atomic decrement**.

## Bug one: tracking a single quantity

```csharp
// ✗ One number cannot express the state.
public int Quantity { get; private set; }
```

Decrement it when the order is *submitted* and your warehouse report is wrong: the goods are still on
the shelf, but the system says they left. Decrement it when the picker *takes* them and you oversell:
between submission and picking, the same units are sellable to somebody else.

There is no correct moment, because one number is being asked to answer two different questions.

```csharp
public int QuantityOnHand   { get; private set; }   // physically on the shelf
public int QuantityReserved { get; private set; }   // of those, promised to submitted orders
public int QuantityAvailable => QuantityOnHand - QuantityReserved;   // what you may still sell
```

```
   OnHand     ████████████████████  20   physically on the shelf
   Reserved   ████████               8   promised to submitted orders
   Available  ········████████████  12   = OnHand − Reserved   ← what you may sell

   submit an order for 3    Reserved +3       Available 12 → 9    nothing has physically moved
   a picker takes them      OnHand −3         Available  9 → 9    ← the invariant that proves
                            Reserved −3                             the model is consistent
```

**Picking changes both `OnHand` and `Reserved` by the same amount, so `Available` does not move.**
That invariant is the test of the model: if a physical action changed what you may sell, you have
modelled it wrong.

The reservation is released, not deducted, when an order is cancelled — `Release`, not `Reserve` with
a negative. Two named operations, each with its own guard, beats one method whose meaning depends on
the sign of its argument.

## Bug two: check, then act

Even with three numbers, this oversells:

```csharp
StockItem stock = await repository.GetAsync(productId, ct);

if (stock.QuantityAvailable < quantity)          // ① check
    return InventoryErrors.InsufficientStock(...);

stock.Reserve(quantity);                         // ② act
await unitOfWork.SaveChangesAsync(ct);
```

Two requests for the last unit interleave between ① and ②. Both read `Available = 1`, both pass the
check, both reserve, and `Reserved` is now 2 against `OnHand` of 1. Nothing threw. The `if` was true
when it ran and false by the time it mattered.

**This is the general shape of every concurrency bug**: a decision made on a value that can change
before the decision is used. Recognise it in code review and you will find most of them.

## The three fixes, and when each is right

### 1. Optimistic — the version check

Put a `RowVersion` on the aggregate and let the save fail
([previous chapter](04-concurrency.md)). Correct, and it makes the *last* buyer retry. That is fine
when conflicts are rare. On the last unit of a popular product at 09:00 on launch day, conflicts are
not rare — they are the workload, and a retry storm is what you have built.

### 2. Atomic — push the decision into the database

The strongest answer when it fits, because there is no window at all:

```csharp
int rowsAffected = await db.StockItems
    .Where(s => s.ProductId == productId
             && s.WarehouseId == warehouseId
             && s.QuantityOnHand - s.QuantityReserved >= quantity)   // the check…
    .ExecuteUpdateAsync(s => s.SetProperty(
        x => x.QuantityReserved,
        x => x.QuantityReserved + quantity), ct);                     // …and the act, one statement

if (rowsAffected == 0)
    return InventoryErrors.InsufficientStock(productId, quantity, 0);
```

The condition is in the `WHERE` clause, so the database evaluates it while holding the row lock it
needs for the update anyway. **`rowsAffected == 0` is the failure signal** — either the row is gone or
there was not enough stock, and either way you did not oversell.

Two caveats to state honestly: `ExecuteUpdate` **bypasses the change tracker**, so no domain events
are raised and no [save interceptor](../module-06-efcore/08-interceptors.md) fires — you must raise
the consequences yourself. And the rule now lives in a LINQ expression rather than in `StockItem`,
which is a real loss of expressiveness paid for correctness under contention.

### 3. Pessimistic — lock the row

```sql
SELECT QuantityOnHand, QuantityReserved
FROM   logiflow.StockItems WITH (UPDLOCK, ROWLOCK)
WHERE  ProductId = @p AND WarehouseId = @w;
```

`UPDLOCK` takes the update lock at read time, so a second transaction waits rather than reading a
value about to change. Correct, serialises access to that row, and is the right call when conflicts
are *likely* and a retry is expensive. It is also the classic deadlock source if two code paths lock
rows in different orders — so if you go here, fix an ordering and write it down.

## Why this repository breaks its own rule

[Aggregates](../module-05-clean-architecture/03-aggregates.md) says one transaction, one aggregate,
with eventual consistency between them via domain events. Reserving stock deliberately runs **inside
the same transaction** as submitting the order.

The reasoning, which is the point of the section:

- Eventual consistency here means a window where an order is submitted and stock is not yet reserved.
- Anything can be sold twice inside that window.
- An oversell costs a customer, a refund and a support call. A slightly longer transaction and the
  occasional lock costs milliseconds.

So the rule is broken **once, deliberately, with the reason recorded**. That is the difference
between engineering and dogma, and "when did you break your own architectural rule and why" is a
question that separates candidates who have shipped from candidates who have read.

The alternative — reserve eventually and compensate when it fails — is legitimate and is what large
marketplaces do, because at their scale the locks cost more than the occasional apology. Both answers
are defensible; only "I never thought about it" is not.

## Try it

```bash
dotnet run --project src/LogiFlow.Api
```

Set a product's stock to 1, then fire two submissions at once:

```bash
curl -X POST .../orders/{a}/submit & curl -X POST .../orders/{b}/submit & wait
```

One succeeds, one gets a 409 or an insufficient-stock error, and `QuantityReserved` never exceeds
`QuantityOnHand`. Then remove the guard from `StockItem.Reserve`, replace the atomic path with
check-then-act, and run it again in a loop of twenty — the oversell appears within a few iterations,
and not on every run, which is exactly why this survives testing.

## What to remember

- One quantity cannot answer both "what is on the shelf" and "what may I sell".
- `Available = OnHand − Reserved`; picking moves both, so `Available` does not change.
- Check-then-act is the shape of every concurrency bug. Look for it in review.
- Optimistic: fine when conflicts are rare, a retry storm when they are not.
- Atomic `ExecuteUpdate` with the condition in the `WHERE`: no window, but no change tracker either.
- Pessimistic `UPDLOCK`: right when conflicts are likely; order your locks or expect deadlocks.
- `rowsAffected == 0` is a first-class result, not an edge case.
- Breaking an architectural rule is fine. Doing it without writing down why is not.

**Code:** [`StockItem.cs`](../../src/LogiFlow.Domain/Inventory/StockItem.cs) ·
[`InventoryFeatures.cs`](../../src/LogiFlow.Application/Features/Inventory/InventoryFeatures.cs)

**Next:** [6. Pagination that does not lie](06-pagination.md)
