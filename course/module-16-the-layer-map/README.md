# Module 16 — The layer map: C#, LINQ, SQL and ASP.NET Core

> **Read this twice: once before module 01, and once after module 15.**
>
> The first read gives you a map. The second read is when it means something, because by then you
> have seen every piece of it running.

---

## 1. First, undo the question

"What is the difference between C#, SQL, LINQ and ASP.NET Core?" is asked constantly, and the
honest first answer is that **they are not four options competing for the same job.** Asking which
is better is like asking whether you would rather have a language, a filing system, a translator or
a receptionist.

| | What kind of thing it is | Who executes it | Could you build a system without it? |
|---|---|---|---|
| **C#** | A programming language | The .NET runtime, in **your** process | No — it is what everything else is written in or called from |
| **SQL** | A separate declarative query language | The **database engine**, in a different process, often on a different machine | Only by choosing a different database |
| **LINQ** | A query *syntax and API inside C#*, plus a translation mechanism | Either your process **or** — via a provider — the database | Yes. It is a convenience, and a very good one |
| **ASP.NET Core** | A framework for receiving HTTP and producing responses | Your process, driven by an HTTP server | Yes, if the system is not reached over HTTP |

They stack. They do not compete.

```
   ┌───────────────────────────────────────────────────────────────────┐
   │  HTTP  ·  JSON  ·  the public contract                            │
   │  ── ASP.NET Core ────────────── routing, binding, auth, status ── │
   └──────────────────────────┬────────────────────────────────────────┘
                              │  a bound C# object
   ┌──────────────────────────▼────────────────────────────────────────┐
   │  Your application and domain logic                                │
   │  ── C# ──────────────── rules, validation, orchestration ───────  │
   └──────────────────────────┬────────────────────────────────────────┘
                              │  a C# expression tree describing a query
   ┌──────────────────────────▼────────────────────────────────────────┐
   │  ── LINQ + EF Core ──── the TRANSLATOR. This is the border post.  │
   └──────────────────────────┬────────────────────────────────────────┘
                              │  a SQL string + parameters, over TDS
   ┌──────────────────────────▼────────────────────────────────────────┐
   │  ── SQL Server ──────── planning, indexes, locks, durability ───  │
   │  a different process, a different language, a different machine   │
   └───────────────────────────────────────────────────────────────────┘
```

**Every hard bug in an enterprise .NET application lives at one of those horizontal lines**, not
inside a box. That is what this module is about.

---

## 2. What each one is, and is not

### C# — the language

**Is:** a statically typed, garbage-collected, object-and-functional language. Classes, records,
generics, `async`/`await`, pattern matching, `Span<T>`.

**Is not:** the platform. **.NET** is the runtime, the JIT, the GC and the base class library. C# is
one language that targets it (F# and VB.NET are others). "A .NET developer" is a statement about the
platform; "a C# developer" is a statement about the language. In job adverts they mean the same
thing — see module 17.

**Owns:** everything a rule needs to be expressed precisely. 📂 [`Domain/`](../../src/LogiFlow.Domain/)
has zero NuGet dependencies, on purpose. The rule "you cannot cancel a shipped order" is C# and only
C#, and it can be tested in milliseconds with no database.

### SQL — a different language, run by a different program

**Is:** a declarative language for sets. You describe the result; the engine's optimiser decides
*how* — which index, which join algorithm, in which order. That decision is remade for every
execution plan and can change under you when statistics change.

**Is not:** a slower way of doing what C# does. It runs where the data already is, with the indexes,
the statistics, the locks, and the transaction log. **Moving a million rows across a network so C#
can count them is the single most common performance mistake in this entire field.**

**Owns:** set operations at scale, durability, and — critically — **arbitration between concurrent
processes.** Only the database can decide who wins when two API instances reserve the last unit of
stock at the same moment. No amount of C# can do that, because your two processes cannot see each
other. 📂 [`Domain/Common/AggregateRoot.cs`](../../src/LogiFlow.Domain/Common/AggregateRoot.cs)

> **SQL vs T-SQL vs SQL Server.** SQL is the standard; T-SQL is Microsoft's dialect of it;
> SQL Server is the engine that speaks T-SQL. PostgreSQL speaks PL/pgSQL. The `SELECT`s are portable;
> almost nothing else is.

### LINQ — C# that can be *read* instead of run

This is the one people misunderstand, and it deserves its own section (§4).

**Is:** a set of standard operators (`Where`, `Select`, `GroupBy`, `Join`…) that work over two
completely different things: **objects in memory**, and **a queryable data source**.

**Is not:** SQL. Not a database feature. Not an ORM. `File.ReadLines(path).Where(l => l.Length > 80)`
is LINQ with no database in sight.

**Owns:** expressing "what I want" once, in a form that can either be executed here or shipped
somewhere else to be executed. Nothing more, and nothing less.

### ASP.NET Core — the edge

**Is:** a host, a DI container, an HTTP server and a middleware pipeline (module 15).

**Is not:** aware that SQL exists. Search the entire 📂 [`Api/`](../../src/LogiFlow.Api/) project for
`SELECT` — there is none, and there should be none. It converts HTTP into a C# call and a `Result`
back into a status code. That is its whole job.

**Owns:** everything that is true *because the request arrived over HTTP* — status codes, content
negotiation, authentication, CORS, rate limiting, caching headers.

---

## 3. One request, all four layers

`POST /api/orders/{id}/submit`, traced through the real code in this repository.

| # | Layer | What happens | Where |
|---|---|---|---|
| 1 | **HTTP** | Bytes arrive at Kestrel and become an `HttpContext` | — |
| 2 | **ASP.NET Core** | Middleware runs in order: exceptions → logging → rate limit → authn → authz | 📂 [`Program.cs`](../../src/LogiFlow.Api/Program.cs) |
| 3 | **ASP.NET Core** | Routing matches the endpoint; `{orderId:guid}` binds to a C# `Guid` | 📂 [`OrderEndpoints.cs`](../../src/LogiFlow.Api/Endpoints/OrderEndpoints.cs) |
| 4 | **C#** | A `SubmitOrderCommand` record is constructed. **HTTP ends here.** | same |
| 5 | **C#** | The dispatcher finds the handler and wraps it in behaviours | 📂 [`Dispatcher.cs`](../../src/LogiFlow.Application/Abstractions/Messaging/Dispatcher.cs) |
| 6 | **C#** | `ValidationBehavior` rejects a malformed command → 400, never reaching the domain | 📂 [`Behaviors/`](../../src/LogiFlow.Application/Behaviors/) |
| 7 | **C#** | `TransactionBehavior` opens a transaction | same |
| 8 | **LINQ** | `context.Orders.Include(o => o.Lines)` — **this builds an expression tree. Nothing has run** | 📂 [`OrderRepository.cs`](../../src/LogiFlow.Infrastructure/Persistence/Repositories/OrderRepository.cs) |
| 9 | **EF Core** | `await …FirstOrDefaultAsync(o => o.Id == id, ct)` — **the border.** The tree becomes a parameterised SQL string | same |
| 10 | **SQL** | The engine parses, plans, takes locks, reads pages, returns rows over TDS | SQL Server |
| 11 | **EF Core** | Rows are materialised into `Order` objects and registered with the change tracker | — |
| 12 | **C#** | `order.Submit()` runs the actual business rule. **No SQL, no HTTP, no framework.** | 📂 [`Domain/Orders/`](../../src/LogiFlow.Domain/Orders/) |
| 13 | **C#** | An illegal transition returns `OrderErrors.InvalidTransition(from, to)` — a **value**, not an exception | 📂 [`OrderErrors.cs`](../../src/LogiFlow.Domain/Orders/OrderErrors.cs) · [`Result.cs`](../../src/LogiFlow.Domain/Results/Result.cs) |
| 14 | **EF Core** | `SaveChanges` diffs the tracker and emits `UPDATE … WHERE Id = @p0 AND RowVersion = @p1` | — |
| 15 | **SQL** | 0 rows affected ⇒ someone else changed it ⇒ EF Core throws a concurrency exception | SQL Server |
| 16 | **ASP.NET Core** | `Result` → HTTP: `ErrorType.Conflict` → 409, `NotFound` → 404, `Validation` → 400, success → 204 | 📂 [`ResultExtensions.cs`](../../src/LogiFlow.Api/Infrastructure/ResultExtensions.cs) |

**Read step 16 again.** `ResultExtensions.cs` is *the only file in the solution that knows what a 409
is.* The domain says `Order.InvalidTransition`, and classifies it as a `Conflict`; this file — and
nothing below it — decides that a Conflict is 409. That is what a clean boundary looks like, and it
is why the same domain can be driven by a message queue tomorrow with nothing rewritten.

Note also what the domain returns: a **stable dotted error code**, not a message. A front end that
branches on `error.code === "Order.InvalidTransition"` keeps working when someone improves the
wording; one that matches on the message string breaks silently.

---

## 4. The border post: where LINQ stops being C# and becomes SQL

If you take one thing from this module, take this.

### Two `Where` methods with the same name

```csharp
// System.Linq.Enumerable
Where<T>(this IEnumerable<T> source, Func<T, bool> predicate)

// System.Linq.Queryable
Where<T>(this IQueryable<T>  source, Expression<Func<T, bool>> predicate)
```

`Func<T,bool>` is **compiled code**. You can call it. You cannot read it.
`Expression<Func<T,bool>>` is **a data structure describing the code**. You can walk it, inspect it,
and translate it into another language. That is the entire mechanism by which C# turns into SQL.

📂 [`Domain/Common/Specifications/ExpressionExtensions.cs`](../../src/LogiFlow.Domain/Common/Specifications/ExpressionExtensions.cs)
composes these trees by hand. Read it once and the magic disappears permanently.

### The single most expensive one-word bug in .NET

```csharp
IQueryable<Order>  q = dbContext.Orders;
IEnumerable<Order> e = dbContext.Orders;   // legal — IQueryable<T> derives from IEnumerable<T>

q.Where(o => o.Total > 100).ToList();      // SELECT … FROM Orders WHERE Total > 100   → 12 rows
e.Where(o => o.Total > 100).ToList();      // SELECT * FROM Orders  → 4,000,000 rows, then filter
```

**Identical syntax. Identical result. One reads twelve rows and the other reads the table.**

```
   IQueryable<Order> q = db.Orders;          IEnumerable<Order> e = db.Orders;
            │                                          │
            │ .Where(o => o.Total > 100)               │ .Where(o => o.Total > 100)
            ▼                                          ▼
   Queryable.Where(Expression<Func<…>>)       Enumerable.Where(Func<…>)
   a TREE the provider can read               a DELEGATE it cannot see inside
            │                                          │
            ▼                                          ▼
   SELECT … FROM Orders WHERE Total > 100     SELECT * FROM Orders
   12 rows cross the wire                     4,000,000 rows cross the wire,
                                              then 3,999,988 are discarded in your process

   the compiler chose the extension method from the STATIC TYPE of the variable.
   Same syntax. Same answer. Both still lazy, so nothing looks wrong until you read the SQL.
```

The compiler picked a different extension method purely from the *static type* of the variable. Both
are still lazy, so nothing looks wrong until you watch the SQL. This is why:

- `var` matters here — an accidental `IEnumerable<T>` in a method signature or a repository return
  type silently drags the whole table into memory
- returning `IQueryable<T>` from a repository is a real architectural decision, not a style one: it
  keeps composition open, and it leaks EF Core into whoever calls it

### Where the border is, precisely

The query executes in the database until **one** of these happens:

| Trigger | Effect |
|---|---|
| `ToList()`, `ToArray()`, `First()`, `Count()`, `Any()`, `foreach`, `await …Async()` | Executes now, in SQL |
| `AsEnumerable()` | **Everything after this line runs in C# on the rows fetched so far** |
| Assigning to an `IEnumerable<T>` variable or parameter | Same as above, but invisible |
| Calling a method EF Core cannot translate | EF Core **throws** — see below |

### EF Core will not silently fall back — with one exception

Since EF Core 3.0, an untranslatable expression in a `Where`, `OrderBy` or `Join` throws
`The LINQ expression … could not be translated`. That looks hostile and is a gift: earlier versions
silently fetched the table and filtered in memory, and people found out in production.

**The exception is the final `Select` projection**, where client evaluation is still allowed:

```csharp
.Where(o => IsInteresting(o))                        // throws — cannot translate a C# method
.Select(o => new { o.Id, Label = Describe(o) })      // fine — runs in C# after the rows arrive
```

That asymmetry is deliberate and worth being able to explain: filtering client-side changes *how
much data crosses the wire*, projecting client-side does not.

---

## 5. The same code, different meanings, on either side of the line

This is the part that catches experienced developers, because the C# compiles and looks obviously
correct.

### Nulls: C# has two values, SQL has three

```csharp
a == b   // C#:  null == null  →  true
a = b    -- SQL: NULL = NULL   →  UNKNOWN  →  the row is not returned
```

SQL uses three-valued logic. EF Core knows this and **compensates**, which is why your innocent
`.Where(o => o.Reference != "X")` on a nullable column generates:

```sql
WHERE [o].[Reference] <> N'X' OR [o].[Reference] IS NULL
```

Those `OR … IS NULL` clauses in your generated SQL are not EF Core being clumsy. They are EF Core
preserving C# semantics across a language boundary — and they can cost you an index seek, which is
why `IS NULL` handling belongs in your indexing conversation.

### Strings: the database is probably case-insensitive and you are not

SQL Server's common default collation (`SQL_Latin1_General_CP1_CI_AS`) is **C**ase **I**nsensitive.

```csharp
list.Where(p => p.Name == "widget")        // in memory  → matches nothing
db.Products.Where(p => p.Name == "widget") // in SQL     → matches "Widget", "WIDGET"
```

**The same predicate gives different answers depending on which side of the border it runs.** A unit
test over an in-memory list can pass while production is wrong, or vice versa. This alone is a good
argument against the EF Core in-memory provider for tests, and for the real-SQL-Server approach in
📂 [`LogiFlow.Api.IntegrationTests`](../../tests/).

And do not "fix" it with `.ToLower()`: that makes the predicate non-sargable and the index unusable.

### Ordering, decimals, dates

- **`OrderBy` in LINQ to Objects is stable. SQL `ORDER BY` is not.** Equal keys can come back in a
  different order between executions, so paging on a non-unique sort key silently repeats and skips
  rows. Always append a tiebreaker: `.OrderBy(o => o.CreatedAt).ThenBy(o => o.Id)`. Module 07.
- **No `ORDER BY` means no order.** Not insertion order. Not primary-key order. *No* order.
- **Money is `decimal`, never `double`.** `decimal` is base-10 and maps to `decimal(18,2)`; `double`
  is binary floating point and cannot represent 0.1. 📂 [`Money.cs`](../../src/LogiFlow.Domain/ValueObjects/Money.cs)
- **`GroupBy` means different things.** In LINQ to Objects it hands you the full elements of each
  group. In SQL, `GROUP BY` returns only keys and aggregates. EF Core translates `GroupBy` only when
  it is followed by an aggregate; ask it for the elements and it cannot, and says so.

---

## 6. Which layer should own this?

The design question you will be asked to justify in interviews and in code review.

| Concern | Put it in | Because |
|---|---|---|
| "Quantity must be > 0" | **C# domain** | It is true of an order regardless of storage or transport |
| "Request body must have a productId" | **C# validation behaviour** → 400 | It is about the message, not the business |
| "Only an Admin may do this" | **ASP.NET Core policy** → 403 | It is about the caller, which only the edge knows |
| "This SKU must be unique" | **SQL unique index** (plus a friendly C# pre-check) | Under concurrency, only the database can actually guarantee it |
| "Do not oversell the last unit" | **SQL** via `rowversion` optimistic concurrency | Two processes cannot arbitrate between themselves |
| Filtering, sorting, paging | **SQL**, via LINQ on `IQueryable` | The data is there; the indexes are there |
| Sum of 40 rows already loaded | **C#** | A round trip costs far more than the addition |
| "Send an email on submit" | **C# domain event → outbox** | Not part of the transaction, must not fail it |
| Status codes, `Location` headers | **ASP.NET Core only** | Nothing below the edge should know HTTP exists |

Two rules generalise it:

1. **Push work to where the data is** — unless the data is already here.
2. **A guarantee belongs at the lowest layer that can actually enforce it.** A C# check on
   uniqueness is a nicer error message; the unique index is the guarantee. Keep both, and know which
   is which.

---

## 7. Confusions worth clearing up, in one line each

| | |
|---|---|
| **.NET vs C#** | .NET is the platform and runtime; C# is a language that targets it |
| **.NET Framework vs .NET** | 4.8 is Windows-only and in maintenance; .NET 5+ ("just .NET") is cross-platform and where everything new happens |
| **ASP.NET vs ASP.NET Core** | Different frameworks that share a name. `HttpContext.Current` belongs to the old one |
| **LINQ vs SQL** | LINQ is C#; SQL is a separate language. A provider translates one into the other |
| **LINQ vs EF Core** | LINQ is the query API; EF Core is one **provider** that can translate it to SQL. LINQ over a `List<T>` involves no EF Core at all |
| **EF Core vs Dapper** | EF Core maps, tracks changes and generates SQL; Dapper maps rows to objects from SQL *you* wrote. Many teams use both — EF Core to write, Dapper for hot read paths |
| **`IEnumerable<T>` vs `IQueryable<T>`** | Runs here, over delegates · Runs there, over expression trees |
| **`IQueryable<T>` vs `IAsyncEnumerable<T>`** | A query not yet sent · a stream of results arriving over time |
| **Entity vs DTO** | An entity has identity and rules and is tracked; a DTO is a shape on the wire. Never serialise entities — you leak your schema and invite over-posting |
| **`Func<T,bool>` vs `Expression<Func<T,bool>>`** | Code you can run · data describing code, which you can also translate |
| **Deferred vs lazy vs eager** | Deferred = the query has not run · lazy loading = a navigation property fetched on access (the N+1 machine) · eager = `Include` |

---

## 8. Which layer is the bug in?

A diagnostic table. Worth keeping.

| Symptom | Almost always |
|---|---|
| 401 with a token you just minted | **ASP.NET Core** — middleware order, or issuer/audience/key mismatch |
| 404 on a URL that clearly exists | **ASP.NET Core** — routing; often a failed route constraint |
| 400 "Required parameter was not provided" | **ASP.NET Core** — binding; a non-nullable `[AsParameters]` property |
| Endpoint is fast alone, slow in a list | **LINQ/EF Core** — N+1. Look at the SQL count, not the C# |
| "The LINQ expression could not be translated" | **The border** — you used a C# method SQL cannot express |
| Query fine in dev, times out in prod | **SQL** — missing index, or a plan that changed with data volume |
| Page 2 repeats a row from page 1 | **SQL** — non-deterministic `ORDER BY`. Add a tiebreaker |
| Works alone, wrong under load | **SQL** — isolation level or missing optimistic concurrency |
| Passes in memory, fails against the database | **The border** — collation, null semantics, or `decimal` precision |
| Stale data, memory grows over time | **ASP.NET Core** — a captive dependency: a singleton holding a scoped `DbContext` |
| Correct 200, but the email never arrives | **C#** — a domain event or outbox message not dispatched |

---

## Do it

**1. Watch the border move.** In 📂 [`Labs.Playground`](../../labs/Labs.Playground/), take any EF Core
query and insert `.AsEnumerable()` in the middle of the chain. Log the SQL both ways
(`--filter '*Linq*'` benchmarks, or `.LogTo(Console.WriteLine)` on the context). The `WHERE` clause
disappears from the SQL and reappears in your process.

**2. Prove the collation difference.** Seed a product named `Widget`. Assert that
`db.Products.Where(p => p.Name == "widget")` finds it and that the same predicate over
`db.Products.ToList()` does not. One line of C#, two answers.

**3. Break the ordering.** Remove the `ThenBy` tiebreaker from a paged query, seed 50 orders sharing
a `CreatedAt`, and page through them. Count distinct ids. You will be short.

**4. Draw it from memory.** Close this file and redraw the four-box diagram, then write next to each
box: one thing it owns, and one thing people wrongly put in it. If you can do that, you can answer
almost any architecture question in an interview.

---

## Golden rules

> The card. If you keep one page from this course, keep this one — and module 07's.

1. **They stack, they do not compete.** C# is the language, SQL is a different language run by a
   different program, LINQ is a query API that can run either side, ASP.NET Core is the edge.
2. **Push the work to where the data is — unless the data is already here.** Filtering 4,000,000
   rows in C# is the single most common performance mistake in this field.
3. **The border is the last `IQueryable`.** `AsEnumerable()`, an `IEnumerable<T>` variable, or a
   method signature returning one, moves everything after it into your process.
4. **A guarantee belongs at the lowest layer that can actually enforce it.** A C# uniqueness check
   is a nicer error message; the unique index is the guarantee. Keep both, and know which is which.
5. **Only the database can arbitrate between processes.** Two API instances cannot see each other,
   so "do not oversell the last unit" is `rowversion` and a constraint, not an `if`.
6. **EF Core throws rather than falling back — except in the final `Select`.** Client evaluation
   in a projection does not change how much data crosses the wire; in a `Where` it changes
   everything.
7. **C# has two truth values; SQL has three.** Those `OR … IS NULL` clauses are EF Core preserving
   C# semantics across the border, and they can cost you an index seek.
8. **The database is probably case-insensitive and your `List<T>` is not.** The same predicate
   gives two different answers on either side of the line — which is an argument against the
   in-memory provider all by itself.
9. **No `ORDER BY` means no order.** Not insertion order, not primary-key order. And SQL's
   ordering is not stable, so paging needs a unique tiebreaker.
10. **Money is `decimal`, never `double`**, on both sides of the border.
11. **Only one file in the solution should know what a 409 is.** The domain classifies the error;
    the edge maps the classification to a status code. That is what lets a message queue drive the
    same domain tomorrow.
12. **When something is wrong, name the layer first.** The diagnostic table in §8 turns "it is
    broken" into "it is the border, so look at collation, nulls, precision and ordering".

---

## Interview questions

**"What's the difference between C#, LINQ and SQL?"**
C# is the language; SQL is a separate language executed by the database engine; LINQ is a C# query
API that can either run in memory over delegates or be translated by a provider into SQL. They are
layers, not alternatives.

**"`IEnumerable` or `IQueryable`?"**
`IEnumerable` executes in this process over compiled delegates. `IQueryable` builds an expression
tree that a provider translates. Assign an `IQueryable` to an `IEnumerable` variable and you silently
move filtering from the database into memory — same syntax, catastrophically different query.

**"How does EF Core turn my lambda into SQL?"**
`Queryable`'s operators take `Expression<Func<...>>`, so the compiler emits a *data structure* rather
than a method. The provider walks that tree and builds parameterised SQL. Parameterised, not
concatenated — which is also why it is not SQL-injectable.

**"Where do you put validation?"**
At every layer, defending something different: shape at the edge (400), business rules in the domain
(409), and guarantees the database alone can enforce as constraints. Under concurrency, only the
constraint is real.

**"Why not do the filtering in C#? It's easier to read."**
Because it moves every row across the network and discards them in memory, and it cannot use an
index. Filtering runs where the data is, unless the data is already here.

**"Same query, different results locally and in production — where do you look?"**
The C#/SQL border: collation and case sensitivity, null semantics, `decimal` precision, and ordering
determinism. All four are places where identical C# means different things on either side.

---

**Next:** [Module 17 — Landing a .NET job in Modena and Bologna](../module-17-career-emilia-romagna/)
turns all of this into a plan.
