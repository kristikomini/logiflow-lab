# Module 06 — EF Core in depth

> Where most .NET performance problems are born. Everything here is checkable against the
> running database — turn the SQL logging on and watch.

**Before you start**, set this in `src/LogiFlow.Api/appsettings.Development.json`:

```json
"Microsoft.EntityFrameworkCore.Database.Command": "Information"
```

Every query now prints with its parameters. Leave it on for this whole module.

---

## In this module

The sections below are summaries. Each links to a chapter that goes further — the code, the traps,
and the interview answer.

| | Chapter | |
|---|---|---|
| 1 | [`DbContext` is a session](01-dbcontext-and-change-tracking.md) | tracking, lifetimes, `AsNoTracking` |
| 2 | [Fluent configuration, not attributes](03-fluent-configuration.md) | one file per entity, conventions that prevent silent defaults |
| 3 | [Value conversions and strongly-typed IDs](02-value-conversions.md) | value objects that survive a database |
| 4 | [The N+1 problem](04-n-plus-one.md) | how to spot it, and the three fixes in order |
| 6 | [Repositories and the unit of work](05-repositories-and-uow.md) | the version that is not an anti-pattern |
| 6b | [The specification pattern](06-specification-pattern.md) | composable predicates that still translate to SQL |
| 7 | [Interceptors](08-interceptors.md) | hooks into EF's own pipeline |
| 8 | [The transactional outbox](07-outbox-pattern.md) | the dual-write problem, solved with one table |
| 9 | [Migrations](09-migrations.md) | read them, script them, expand and contract |

---

## 1. `DbContext` is a session, and it is not thread-safe

Three facts that explain most EF Core confusion:

1. **It tracks everything it returns.** Load an entity, change a property, call
   `SaveChangesAsync()` — EF diffs against the snapshot it took and writes only the changed
   columns. You never write an `UPDATE`.
2. **It is scoped to a request.** Registering it as a singleton produces the most confusing bug
   in ASP.NET Core: *"A second operation was started on this context instance before a previous
   operation completed"* — intermittently, under load, never in development.
3. **It is already a Unit of Work plus Repositories.** `DbSet<T>` is a repository;
   `SaveChanges` is a unit of work. The extra interfaces in this codebase exist so the
   Application layer need not reference EF Core — not because `DbContext` lacks the patterns.

📂 [`Infrastructure/Persistence/LogiFlowDbContext.cs`](../../src/LogiFlow.Infrastructure/Persistence/LogiFlowDbContext.cs)

### Tracking vs no-tracking

```csharp
var order = await db.Orders.FirstAsync(...);            // tracked   — for writes
var rows  = await db.Orders.AsNoTracking().ToListAsync(); // untracked — for reads
```

Tracking snapshots every entity so it can detect changes. On a read-only list that is pure
overhead — roughly 20–30% slower and materially more memory.

**The rule: tracking for writes, `AsNoTracking()` for reads.** Every read in
[`OrderQueries.cs`](../../src/LogiFlow.Infrastructure/Persistence/Queries/OrderQueries.cs) uses it.

---

## 2. Fluent configuration, not attributes

`[Column]` and `[MaxLength]` on a domain class drag persistence concerns into the Domain layer
and let schema decisions be made by someone editing business logic. The Fluent API keeps it out,
and can express things attributes cannot: filtered indexes, owned types, converters, check
constraints.

📂 [`Configurations/OrderConfiguration.cs`](../../src/LogiFlow.Infrastructure/Persistence/Configurations/OrderConfiguration.cs)

Three things in there worth understanding:

**Value objects map inline, not as tables.**

```csharp
builder.OwnsOne(o => o.ShippingAddress, address => { ... });
```

produces `ShippingAddress_Line1`, `ShippingAddress_City`, … as columns on `Orders`. No join,
no `AddressId`, no orphan rows. This is *table splitting*.

`OwnsOne` vs `ComplexProperty` (EF 8+): an owned type is still an entity with identity in the
change tracker; a complex type is genuinely part of the parent row. `Money` uses
`ComplexProperty` because €10 has no identity. `Address` uses `OwnsOne` because it is optional
on an order, and owned types have supported optional mapping longest.

**Computed properties are not columns.**

```csharp
builder.Ignore(o => o.Subtotal);   // derived from the lines — storing it invites drift
```

**`decimal` precision must be set, or SQL Server silently truncates.** The model-wide default is
in `ConfigureConventions`:

```csharp
configurationBuilder.Properties<decimal>().HavePrecision(19, 4);
```

Without it EF picks `decimal(18,2)` and quietly drops precision. You find out when the accounts
do not balance.

---

## 3. Value conversions and strongly-typed IDs

EF has no idea what an `OrderId` is. It needs a converter saying "to store this take `.Value`;
to read it, rebuild it".

Writing one per ID type is fourteen things to forget across seven identifiers. Instead, one
convention finds them all by reflection:

📂 [`Conventions/StronglyTypedIdConvention.cs`](../../src/LogiFlow.Infrastructure/Persistence/Conventions/StronglyTypedIdConvention.cs)

That file also contains a genuinely instructive failure. The obvious implementation:

```csharp
: base(id => id.Value, value => TId.From(value))
```

does not compile — **CS8927: an expression tree may not contain an access of static virtual or
abstract interface member**. Static abstract members are resolved per closed generic type at JIT
time; an expression tree is built at compile time, so there is no single method handle to embed.

The fix is to build the trees by hand with `Expression.New` / `Expression.Property`. You will hit
this wall any time generic math or static abstracts meet anything expression-tree-based, and the
answer is always the same: drop to `Expression` and construct the node yourself.

---

## 4. The N+1 problem

The single most common ORM performance defect, and a guaranteed interview question.

```csharp
var orders = await db.Orders.ToListAsync();          // 1 query
foreach (var o in orders)
    Console.WriteLine(o.Lines.Count);                // N more, one per order
```

101 round trips for 100 orders. Each one is a network hop.

```
   N + 1                                          1.  SELECT * FROM Orders
   db.Orders.ToListAsync()                        2.  SELECT * FROM OrderLines WHERE OrderId = 1
   foreach (o in orders) read o.Lines             3.  SELECT * FROM OrderLines WHERE OrderId = 2
                                                      :
                                                101.  SELECT * FROM OrderLines WHERE OrderId = 100
                                                ────────────────────────────────────────────────
                                                101 round trips, each one a network hop

   .Include(o => o.Lines)                           1 round trip, one small cartesian product
   .Select(o => new { … })                          1 round trip, only the columns you asked for
```

**Fixes, in order of preference:**

```csharp
// 1. Include — load the children with the parent
await db.Orders.Include(o => o.Lines).ToListAsync();

// 2. Projection — load only what you need (usually best for reads)
await db.Orders.Select(o => new { o.Id, LineCount = o.Lines.Count }).ToListAsync();

// 3. Split query — when MULTIPLE collection includes multiply into a cartesian product
await db.Orders.Include(o => o.Lines).Include(o => o.Shipments).AsSplitQuery().ToListAsync();
```

📂 [`Repositories/OrderRepository.cs`](../../src/LogiFlow.Infrastructure/Persistence/Repositories/OrderRepository.cs)
— note it deliberately does **not** use `AsSplitQuery` for a single collection include, and says
why: one small cartesian product in one round trip beats two round trips.

### The other N+1: loading in a loop

```csharp
foreach (var id in productIds)
    products.Add(await repo.GetAsync(id));   // ❌ 20 round trips for a 20-line order
```

📂 `ProductRepository.GetManyAsync` exists precisely to kill this — `WHERE Id IN (...)`, one
round trip. Note the caveat in its comments: SQL Server caps at **2,100 parameters** per command,
so above ~2,000 ids you must batch.

---

## 5. Lazy loading: don't

EF can generate proxies that load navigations on access. It sounds convenient and it is a trap:
it turns a property read into a database call, which means N+1 appears *invisibly*, and it
explodes if the context is disposed.

This codebase does not enable it. **Be explicit about what you load.** If a navigation is null,
that is information — you did not ask for it.

---

## 6. Repositories and the specification pattern

The generic `IRepository<T>` with `GetAll()` you meet in tutorials is a bad default:

- It **leaks** — `IQueryable<T>` escaping means queries get written anywhere.
- It **lies** — not every aggregate supports every operation. Products are never deleted.
- It is **redundant** — `DbSet<T>` is already a better generic repository.

Per-aggregate interfaces with intention-revealing names are used here instead:
`GetWithLinesAsync` says exactly what it loads; a generic `GetById` does not, and the caller
finds out when a navigation is null.

For the combinatorial-filter problem, a **specification** makes the criteria the parameter:

```csharp
var spec = new OrdersInStatusSpec(Submitted).And(new HighValueOrdersSpec(1000m));
var orders = await repo.ListAsync(spec);
```

📂 [`Domain/Common/Specifications/Specification.cs`](../../src/LogiFlow.Domain/Common/Specifications/Specification.cs)

The two evaluation modes are the point: `ToExpression()` hands EF a tree it can turn into SQL;
`IsSatisfiedBy()` runs the same rule in memory so a unit test can verify the business rule with
a plain object. One definition, guaranteed to agree.

**Know the counter-argument.** Specifications add a layer, and for a handful of simple queries
they are overhead. They earn their keep when the same rule appears in several places or when
rules compose combinatorially.

---

## 7. Interceptors

📂 [`Interceptors/DomainEventDispatchingInterceptor.cs`](../../src/LogiFlow.Infrastructure/Persistence/Interceptors/DomainEventDispatchingInterceptor.cs)

Why an interceptor rather than calling dispatch from each handler? Because it would have to be
called from *every* handler, and the one someone forgets is the one whose events silently never
fire. An interceptor cannot be bypassed.

**`SavingChanges`, not `SavedChanges`** — and that is the subtle part. Events dispatch *before*
the write completes, so handlers run inside the same transaction: if one throws, everything rolls
back. That is why an order cannot be submitted without its stock being reserved.

The cost: a slow handler holds the transaction open. Anything slow or external must use the
outbox instead.

---

## 8. The transactional outbox

**The dual-write problem.** You need to commit a change *and* tell another system:

```csharp
await db.SaveChangesAsync();          // 1. commits
await bus.PublishAsync(orderPlaced);  // 2. process dies here → order exists, nobody told
```

Swap the order and you get the mirror bug: a message for an order that was never committed.
There is **no ordering of two systems that is safe**, because you cannot commit both atomically.

```
   DUAL WRITE — unsafe in either order         OUTBOX — one write, then a worker
   ┌───────────────────┐                       ┌──────────────────────────────────────┐
   │ SaveChangesAsync  │  committed            │  ONE transaction:                    │
   └─────────┬─────────┘                       │     UPDATE  Orders          …        │
             │   process dies here             │     INSERT  OutboxMessages  …        │
   ┌─────────▼─────────┐                       └──────────────────┬───────────────────┘
   │ bus.PublishAsync  │  never happens                           │
   └───────────────────┘                          a background worker reads unprocessed rows
                                                                  │
   the order exists and nobody was told                  publish ─┴─► stamp ProcessedAtUtc

   swap the two statements and you get the        at-least-once delivery, which is why
   mirror bug: a message about an order           consumers must be idempotent — the event
   that was rolled back                           id is carried as the deduplication key
```

The outbox makes it one write: the message is inserted as a row *in the same transaction*. A
background worker publishes it afterwards.

📂 [`Outbox/OutboxMessage.cs`](../../src/LogiFlow.Infrastructure/Persistence/Outbox/OutboxMessage.cs) ·
[`Outbox/OutboxProcessor.cs`](../../src/LogiFlow.Infrastructure/Persistence/Outbox/OutboxProcessor.cs)

**The guarantee is at-least-once, not exactly-once.** Die after publishing but before marking the
row processed and it publishes again. Exactly-once is impossible in a distributed system; the
practical answer is at-least-once plus **idempotent consumers**, which is why the event id is
carried as a deduplication key.

Two production details in `OutboxProcessor` worth stealing:

- It is a singleton `BackgroundService`, so it **cannot inject a scoped `DbContext`** — it uses
  `IServiceScopeFactory` and creates a scope per iteration.
- Deserialisation goes through a **type allow-list**. Feeding a type name from data straight into
  `Type.GetType()` is the classic insecure-deserialisation vulnerability.

You can see it work:

```bash
sqlcmd -S localhost -E -C -d LogiFlow \
  -Q "SELECT Type, ProcessedAtUtc FROM logiflow.OutboxMessages"
```

---

## 9. Migrations

```bash
dotnet ef migrations add AddSomething --project src/LogiFlow.Infrastructure --output-dir Persistence/Migrations
dotnet ef database update --project src/LogiFlow.Infrastructure
```

📂 [`DesignTimeDbContextFactory.cs`](../../src/LogiFlow.Infrastructure/Persistence/DesignTimeDbContextFactory.cs)
exists so the tools do not have to boot your web application to find a `DbContext`. Without it you
get the famously unhelpful *"Unable to create an object of type 'LogiFlowDbContext'"*.

**Rules learned the hard way:**

- **Always read the generated migration** before applying it. EF sometimes decides a rename is a
  drop-and-recreate, which silently deletes data.
- **Never edit an applied migration.** Add a new one.
- **Migrations run in CI as a separate step**, not at application startup. `Program.cs` migrates
  on boot *in Development only*, and says why: several instances starting at once race to migrate
  the same database, and the app then needs schema-altering permissions it should not have.

---

## 10. Golden rules

> The card. Most .NET performance problems are one of these being ignored.

1. **Tracking for writes, `AsNoTracking()` for reads.** Snapshotting entities you will never
   change costs 20–30% and more memory, for nothing.
2. **`DbContext` is a session: scoped, and not thread-safe.** Register it as a singleton and you
   get a bug that appears only under load, only in production.
3. **Configure with the Fluent API, not attributes.** It keeps persistence out of the Domain and
   expresses what attributes cannot: converters, filtered indexes, check constraints.
4. **Set `decimal` precision explicitly.** The default silently truncates, and you find out when
   the accounts do not balance.
5. **Value objects map inline, as columns** — not as a table with its own id and orphan rows.
6. **Be explicit about what you load.** `Include` or project; never lazy loading, which turns a
   property read into a query and makes N+1 invisible.
7. **Prefer a projection to `Include` for reads.** Fetch the six columns the screen needs, not the
   aggregate and all its children.
8. **Never query inside a `foreach`.** One `WHERE Id IN (…)` — and mind SQL Server's
   2,100-parameter ceiling.
9. **Per-aggregate repositories, with intention-revealing names.** `GetWithLinesAsync` tells the
   caller what it loads; a generic `GetById` lets them find out when a navigation is null.
10. **Anything that must never be forgotten belongs in an interceptor.** The handler someone
    forgets to call is the one whose events silently never fire.
11. **Anything crossing a process boundary goes through the outbox.** You cannot commit two
    systems atomically, so make it one write and publish afterwards.
12. **Read every generated migration, and never edit an applied one.** EF sometimes decides a
    rename is a drop-and-recreate, which silently deletes a column of data.

---

## 11. Interview questions

**"What is the N+1 problem and how do you fix it?"**
One query for parents plus one per child collection. Fix with `Include`, or better, a projection
that fetches only the needed columns. Mention that lazy loading causes it invisibly, and that
loading in a `foreach` is the same bug in a different shape.

**"Tracking vs no-tracking?"**
Tracking snapshots entities so `SaveChanges` can diff them. Use it for writes. `AsNoTracking` for
reads — 20–30% faster and less memory. No-tracking also disables identity resolution, so two
references to one row give two objects.

**"How does `SaveChanges` know what to update?"**
The change tracker holds an original-values snapshot from materialisation and compares it,
generating `UPDATE` for only the changed columns.

**"Repository pattern over EF Core — worth it?"**
Have a real opinion. `DbContext` already implements Unit of Work and Repository, so a *generic*
repository adds nothing. Per-aggregate repositories are worth it when you want the Application
layer free of EF Core, want intention-revealing data access in one place, and want to unit-test
handlers without a database. If you are happy depending on EF everywhere, skip it.

**"Explain the outbox pattern."**
It solves the dual-write problem by writing the message as a row in the same transaction as the
state change, then publishing it from a background worker. Gives at-least-once delivery, which
requires idempotent consumers.

---

## Next

→ [Module 07 — SQL, transactions and concurrency](../module-07-sql-and-transactions/)
