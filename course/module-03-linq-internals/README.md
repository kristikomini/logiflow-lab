# Module 03 — LINQ internals

> The module that separates people who *use* LINQ from people who *understand* it.
> If you only do one module properly, do this one — the misunderstandings here cause more
> production incidents than any other topic in .NET.

---

## Deeper chapters

| | Chapter | |
|---|---|---|
| 4 | [Expression trees, and what EF Core can translate](03-expression-trees.md) | why a stray `AsEnumerable()` moves your filter into memory |

---

## 1. LINQ is not a feature. It is two interfaces and a compiler trick.

Everything in LINQ reduces to extension methods over two types:

```csharp
IEnumerable<T>    // in-memory sequences. Operators take Func<...>       → compiled delegates
IQueryable<T>     // remote sources.     Operators take Expression<Func<...>> → data structures
```

That single difference — **delegate versus expression tree** — is the whole subject.

```csharp
Func<Order, bool>             compiled = o => o.Total > 100;   // executable code, a black box
Expression<Func<Order, bool>> tree     = o => o.Total > 100;   // a DESCRIPTION of that code
```

Identical syntax. Completely different objects. The first is a method pointer you can only
invoke. The second is a tree you can walk:

```
        LambdaExpression
              │
       BinaryExpression (GreaterThan)
        ┌─────┴─────┐
  MemberExpression   ConstantExpression
   (o.Total)              (100)
```

EF Core walks that tree and emits `WHERE Total > 100`. Hand it a compiled `Func` instead and it
cannot see inside — so it must fetch **every row** and filter in memory.

**This is the single most common cause of a .NET application that works in development and dies
in production.** It is invisible with 50 seeded rows and fatal with 5 million real ones.

📂 Read: [`Domain/Common/Specifications/ExpressionExtensions.cs`](../../src/LogiFlow.Domain/Common/Specifications/ExpressionExtensions.cs)
— it builds expression trees by hand and explains why the obvious approach fails.

---

## 2. Deferred execution

A LINQ query is a **recipe, not a result**. Nothing runs until you enumerate.

```bash
cd labs/Labs.Playground && dotnet run deferred
```

You will see the query built with *no* output, then evaluated on enumeration, then evaluated
**again** on the second enumeration. Two companion demos go further:
`dotnet run iterators` shows the state machine, the deferred `throw` and the `finally` that runs
on `Dispose`; `dotnet run expressions` shows a tree being read, rewritten and compiled.

```
   line 1   var q = source.Where(...)     │  nothing runs. q is a RECIPE
   line 2   source.Add(99)                │  still nothing
   line 3   foreach (var x in q) { }      │  ◄── NOW it runs — and sees the 99
   line 4   q.Count()                     │  ◄── runs AGAIN, from the top
                                          │
            build ────────────────────────┴──► execute ──► execute ──► …
              once                            once per enumeration
```

Three consequences that bite in real code:

**(a) Double enumeration = double the work.**

```csharp
var orders = db.Orders.Where(o => o.Status == Submitted);   // no query yet
Console.WriteLine(orders.Count());                          // SELECT COUNT(*) — round trip 1
foreach (var o in orders) { }                               // SELECT * — round trip 2
```

Fix: `ToList()` once when you need the data more than once. But do not reflexively `ToList()`
everything — that pulls the whole table into memory and forfeits server-side filtering. The rule
is: **compose while it is a query, materialise once at the end.**

**(b) The query sees changes made after it was defined.**

```csharp
var list = new List<int> { 1, 2, 3 };
var query = list.Where(n => n > 1);   // [2, 3]
list.Add(99);
// query is now [2, 3, 99] — it re-ran
```

**(c) Captured variables are read at execution time, not definition time.** Combine that with
the closure trap from module 02 and you get bugs that are genuinely hard to see.

### Which operators execute immediately?

| Deferred (lazy) | Immediate (executes now) |
|---|---|
| `Where` `Select` `OrderBy` `GroupBy` `Join` `Take` `Skip` `Distinct` `Concat` | `ToList` `ToArray` `ToDictionary` `Count` `Sum` `Any` `First` `Single` `Max` |

A useful mental rule: **if it returns `IEnumerable<T>`/`IQueryable<T>` it is lazy; if it returns
a concrete value or collection it ran.**

---

## 3. `yield return` — how lazy operators are built

```csharp
static IEnumerable<int> Numbers()
{
    Console.WriteLine("start");
    yield return 1;
    Console.WriteLine("between");
    yield return 2;
}
```

Calling `Numbers()` prints **nothing**. The compiler rewrote that method into a state-machine
class implementing `IEnumerator<int>`, where each `yield return` is a resume point. The body
only advances when `MoveNext()` is called.

Nothing is buffered between operators. The consumer *pulls*, one element at a time, all the way
back to the source:

```
   ToList() ──pull──► Select ──pull──► Where ──pull──► source
            ◄─item──         ◄─item──        ◄─item──

   which is why  .Where(…).Select(…).First()  touches only as many rows as it needs,
   and why a query over 10,000,000 rows can finish before reading the second one
```

### The trap this creates

```csharp
public static IEnumerable<T> Chunk<T>(IEnumerable<T> source, int size)
{
    if (size < 1) throw new ArgumentOutOfRangeException(nameof(size));   // ← never runs on time
    // ...yield return...
}
```

Because the body does not execute until enumeration, that guard throws *far too late* — often in
a completely different part of the program, with a stack trace pointing nowhere useful.

**The fix is the two-method pattern**, used by essentially every operator in the BCL:

```csharp
public static IEnumerable<T> Chunk<T>(IEnumerable<T> source, int size)
{
    ArgumentNullException.ThrowIfNull(source);
    ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
    return Iterator(source, size);            // validate eagerly...

    static IEnumerable<T> Iterator(IEnumerable<T> source, int size)
    {
        // ...yield lazily
    }
}
```

You implement exactly this in **Lab 02, exercise 5**, and a test asserts the eager behaviour.

---

## 4. What EF Core can and cannot translate

An expression tree may only reference things the database understands: mapped columns,
navigations, and a known set of methods.

```csharp
// ✅ translates — Lines is a mapped navigation, Sum has a SQL equivalent
.Where(o => o.Lines.Sum(l => l.UnitPrice.Amount * l.Quantity) >= 1000)

// ❌ does not — Subtotal is a C# property with no column behind it
.Where(o => o.Subtotal.Amount >= 1000)
```

📂 See [`Domain/Orders/OrderSpecifications.cs`](../../src/LogiFlow.Domain/Orders/OrderSpecifications.cs)
— `HighValueOrdersSpec` is written the first way *specifically* because of this, and says so.

Since EF Core 3.0 an untranslatable query **throws** rather than silently falling back to client
evaluation. That was a breaking change and it was the right call: the silent version turned a
typo into a table scan nobody noticed until the table got big.

### Seeing the SQL

Set this in `appsettings.Development.json` and every query prints with its parameters:

```json
"Microsoft.EntityFrameworkCore.Database.Command": "Information"
```

Do this while working through module 09. Reading the generated SQL is the fastest way to build
an accurate mental model of what your LINQ actually costs.

---

## 5. Composition — the pattern worth stealing

Because a query is data, you can build it up conditionally and it stays one SQL statement:

```csharp
IQueryable<Order> orders = context.Orders.AsNoTracking();

if (query.Status is { } status)     orders = orders.Where(o => o.Status == status);
if (query.FromUtc is { } from)      orders = orders.Where(o => o.CreatedAtUtc >= from);
if (query.CustomerId is { } custId) orders = orders.Where(o => o.CustomerId == custId);

var page = await orders.Skip(...).Take(...).ToListAsync();   // ONE query, exactly these filters
```

📂 Read: [`Infrastructure/Persistence/Queries/OrderQueries.cs`](../../src/LogiFlow.Infrastructure/Persistence/Queries/OrderQueries.cs)

The alternative you will meet in older codebases is one SQL string with
`WHERE (@status IS NULL OR Status = @status)` per filter. SQL Server caches **one** plan for
that, and it is usually bad for every combination — the classic parameter-sniffing problem.

---

## 6. Do the lab

```bash
dotnet test labs/Labs.Exercises --filter Lab02
```

Seven exercises in [`labs/Labs.Exercises/Exercises/Lab02_Linq.cs`](../../labs/Labs.Exercises/Exercises/Lab02_Linq.cs).
The interesting ones:

- **#3** — `LargestOrderId` on an empty sequence. `First()` and `Max()` both throw. Which
  operators do not, and what does `MaxBy` give you?
- **#4** — prove deferred execution: build a query that evaluates *nothing* until enumerated.
- **#5** — write `ChunkBy` with correct eager validation. This is the two-method pattern above.
- **#6** — `OrphanedSkus`. Do it with `Except`, then work out why
  `Where(x => !list.Contains(x))` is O(n·m) and what that means at 10,000 × 10,000.

---

## 7. Golden rules

> The card. Module 03 is the one that shows up in production incidents; know these cold.

1. **`IEnumerable<T>` takes delegates and runs here. `IQueryable<T>` takes expression trees and
   runs there.** Assigning one to the other silently moves the work — and the whole table.
2. **Compose while it is a query; materialise once, at the end.** Every `ToList()` in the middle
   of a chain is a decision to stop using the database.
3. **A query is a recipe, not a result.** It re-runs on every enumeration, and it reads captured
   variables at execution time, not at definition time.
4. **If it returns a sequence it is lazy; if it returns a value or a collection it already ran.**
   That one rule covers every operator you will meet.
5. **Never enumerate twice by accident.** `.Count()` then `foreach` is two round trips to the
   database for one answer.
6. **Validate eagerly, yield lazily.** Argument checks go in a wrapper method that returns the
   iterator — otherwise they throw at enumeration, somewhere else entirely.
7. **Never return an `IQueryable<T>` past the lifetime of its `DbContext`.** The caller enumerates
   it after disposal and gets an exception with nothing useful in it.
8. **When EF Core says it cannot translate, it is doing you a favour.** Before 3.0 it silently
   fetched the table instead, and people found out in production.
9. **`Single` is not a stricter `First`.** It must scan the whole source to prove there is no
   second match.
10. **Read the generated SQL.** Every belief you hold about what a query costs is a hypothesis
    until you have.

---

## 8. Interview questions

**"What is the difference between `IEnumerable<T>` and `IQueryable<T>`?"**
`IEnumerable<T>` operators take compiled delegates and run in memory. `IQueryable<T>` operators
take expression trees, which a provider translates to another language — SQL, usually. Passing a
`Func` where a provider expects an `Expression` forces client evaluation: the whole table comes
into memory. Mention that as the failure mode; it is what they are checking for.

**"What is deferred execution, and when has it bitten you?"**
A query does not run until enumerated. It bites through double enumeration (`.Count()` then
`foreach` = two round trips), through capturing a variable that changes before execution, and
through returning an `IQueryable` from a method whose `DbContext` is disposed by the time the
caller enumerates it.

**"How does `yield return` work?"**
The compiler rewrites the method into a state machine class implementing `IEnumerator<T>`, with
each `yield return` as a resume point. Consequence: the body does not run until `MoveNext()`, so
argument validation must live in a separate non-iterator wrapper method.

**"Why can't EF Core translate this?"**
Because it references something with no database representation — a computed C# property, a
local method, a constructor. The provider walks the expression tree and can only map nodes it
recognises.

**"`First` vs `Single` vs `FirstOrDefault`?"**
`First` returns the first match and stops; throws if none. `Single` requires exactly one and
therefore must scan the *whole* source to prove there is no second — it is not a stricter
`First`, it is a full scan. The `OrDefault` variants return `default` instead of throwing.
Benchmarked in `labs/Labs.Benchmarks --filter '*FirstVsSingle*'`.

---

## Next

→ [Module 04 — Async and concurrency](../module-04-async/)
