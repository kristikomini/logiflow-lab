# 3. Optimistic concurrency — the lost update

> Part of [Module 07 — SQL, transactions and concurrency](README.md), section 3.
> Previous: [2. Transactions and isolation levels](02-unit-of-work.md) ·
> Next: [4. Inventory: where oversells come from](05-inventory-concurrency.md)

---

Two users open the same order at 10:00. One changes the shipping address and saves at 10:02. The
other changes the customer reference and saves at 10:03.

The second save writes every column it loaded at 10:00 — including the old address. The first user's
change is gone, with no error, no log line, and no way to know it happened. That is the **lost
update**, and it is the concurrency bug you will actually meet, because it needs no unusual
isolation level and no load. Two users and a slow lunch is enough.

## Why a transaction does not help

Instinct says wrap it in a transaction. It does not help, because the two saves are not concurrent —
they are three minutes apart, and each is individually a perfectly valid, committed transaction.

The only alternative that *would* work is a pessimistic lock held across the human's think time:

```sql
BEGIN TRAN;
SELECT * FROM logiflow.Orders WITH (UPDLOCK) WHERE Id = @id;
-- …now display a form and wait for a person to finish typing…
```

Which means a database lock held for three minutes, blocking everyone else, and left dangling forever
if the user closes the tab. Nobody does this on purpose.

## The optimistic answer

Assume conflicts are rare. Do not lock. Instead, **detect at write time whether the row changed since
you read it**, and refuse the write if it did.

```csharp
public abstract class AggregateRoot<TId> : Entity<TId>, IHasDomainEvents
{
    public byte[]? RowVersion { get; private set; }
}
```

```csharp
builder.Property(o => o.RowVersion).IsRowVersion();
```

`rowversion` is a SQL Server type: an 8-byte value that **the database** bumps on every `UPDATE`. You
never set it. EF reads it with the row and adds it to the `WHERE` clause on save:

```sql
UPDATE logiflow.Orders
SET    ShippingAddress_City = @p0, RowVersion = …
WHERE  Id = @p1 AND RowVersion = @p2;   -- ← the value read at 10:00
```

If somebody else saved in between, the stored `RowVersion` no longer matches, **zero rows are
affected**, and EF throws `DbUpdateConcurrencyException` instead of silently overwriting.

That is the whole mechanism: the check and the write are one atomic statement, so there is no window
between them.

## Where the token belongs

On the **aggregate root**, not on each row inside it.

Two users editing different lines of the same order is still a conflict, because the invariants they
might break — the total, the 500-line limit — span the whole
[aggregate](../module-05-clean-architecture/03-aggregates.md). Putting the version on `Order` makes
the database enforce exactly the right granularity: one concurrent edit per order, and no false
conflicts between unrelated orders.

This is one of the clearest payoffs of getting aggregate boundaries right — the concurrency unit
falls out of the modelling rather than being a separate design exercise.

## Handling the exception

Catching it is not the interesting part; **deciding what to do** is. There are exactly three
sensible answers and the right one depends on the field:

```csharp
try
{
    await unitOfWork.SaveChangesAsync(ct);
}
catch (DbUpdateConcurrencyException)
{
    return OrderErrors.ConcurrencyConflict;   // → HTTP 409 Conflict
}
```

**1. Tell the user (store wins / client loses).** The default, and right for anything a human typed.
"Somebody else changed this order while you were editing. Here is the current version." Cheap,
honest, and never loses data silently.

**2. Retry (client wins).** Right only when the operation is *idempotent and derived* — recompute
from fresh state and try again. `ExecuteUpdate` with an arithmetic expression is often better than a
retry loop here.

**3. Merge.** Reload, compare field by field, and combine if the two users touched different things.
Expensive to build and to test. Worth it for a few high-value screens; not worth it by default.

**409 Conflict is the correct status code**, and `ErrorType.Conflict` maps to it once in
`ResultExtensions`, so no endpoint has to decide.

## Alternatives to `rowversion`

**A `LastModifiedUtc` column** used as the token. Works, and depends on clock resolution — two saves
within the same tick collide undetectably. Fine for low-traffic screens, weaker than `rowversion`.

**A concurrency token on specific columns** — `.IsConcurrencyToken()` on `Status` rather than a
row-wide version. Fewer false conflicts, because two users editing genuinely unrelated fields both
succeed. More thought, and easy to get wrong by forgetting a field that mattered.

**`ExecuteUpdate` with the check in SQL** — the atomic decrement below. Not really optimistic
concurrency; it is avoiding the read-modify-write entirely, which is better when you can.

**Pessimistic locking** (`UPDLOCK`, `SELECT … FOR UPDATE`). Correct when a conflict is *likely* rather
than rare and a retry is expensive — which is exactly the case argued in
[the next chapter](05-inventory-concurrency.md). Optimistic is the default, not the law.

## The mistake nobody sees

The version has to travel to the client and back. If a `PUT` handler loads the order fresh and saves
it, the check compares the value it read *milliseconds ago* — always current, always passes, and the
lost update is back. The token is only doing its job if it round-trips through the caller:

```jsonc
// GET /orders/1042
{ "id": "…", "shippingAddress": { … }, "rowVersion": "AAAAAAAAB9E=" }

// PUT /orders/1042 — the client returns what it was given
{ "shippingAddress": { … }, "rowVersion": "AAAAAAAAB9E=" }
```

Then the handler applies that value to the loaded entity before saving:

```csharp
db.Entry(order).Property(o => o.RowVersion).OriginalValue = request.RowVersion;
```

Omit this and you have shipped a concurrency token that never detects anything — which is much worse
than not having one, because everybody believes the problem is handled.

## Try it

```bash
dotnet run --project src/LogiFlow.Api
```

`GET` an order and note its `rowVersion`. Update it once — succeeds. Now `PUT` again with the **old**
`rowVersion` you noted first, simulating the second user: `409 Conflict`, and the first user's change
is intact. Then look at the generated `UPDATE` in the SQL log and find `AND RowVersion = @p2` in the
`WHERE` clause.

## What to remember

- The lost update needs no unusual isolation level — two users and a few minutes is enough.
- A transaction cannot fix it; the saves are not concurrent.
- Optimistic concurrency detects at write time instead of locking across think time.
- `rowversion` is maintained by the database and added to the `WHERE` clause by EF.
- Zero rows affected → `DbUpdateConcurrencyException` → HTTP 409.
- The token belongs on the aggregate root, because that is the unit of change.
- The token must round-trip through the client, or it never detects anything.
- Store-wins is the default; retry only for derived values; merge only where it pays.

**Code:** [`AggregateRoot.cs`](../../src/LogiFlow.Domain/Common/AggregateRoot.cs) ·
[`OrderConfiguration.cs`](../../src/LogiFlow.Infrastructure/Persistence/Configurations/OrderConfiguration.cs)

**Next:** [4. Inventory: where oversells come from](05-inventory-concurrency.md)
