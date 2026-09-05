# Module 14 — Performance

> Measure, then optimise. Every claim in this course is checkable, and you should check them.

```bash
cd labs/Labs.Benchmarks
dotnet run -c Release                       # menu
dotnet run -c Release --filter '*Linq*'
```

---

## 1. Measure properly, or do not bother

A `Stopwatch` in a console app measures the JIT warming up, not your code. Whichever loop runs
first looks slower. That is not a subtlety — it is a wrong answer.

**BenchmarkDotNet** does what a Stopwatch cannot:

- runs a warm-up so the JIT has compiled and tiered-up
- runs enough iterations for a statistically meaningful result
- reports variance, so you can tell signal from noise
- **stops the JIT deleting your benchmark** — dead-code elimination will happily remove a loop
  whose result you never use
- with `[MemoryDiagnoser]`, reports allocations exactly

📂 [`labs/Labs.Benchmarks/Program.cs`](../../labs/Labs.Benchmarks/Program.cs)

The playground's `boxing` demo deliberately reports **allocations only**, with a comment
explaining that timings there would be meaningless. That is the honest version.

### Read the results properly

The `Mean` column matters far less than `Allocated` in most server code. A method 20ns slower
that allocates nothing beats one that is faster and produces garbage, because **GC pauses hit
every request, not just this one**.

---

## 2. The measurement that shaped this codebase

```bash
dotnet run -c Release --filter '*ErrorHandling*'
```

| Method | Mean | Allocated |
|---|---|---|
| `WithResults` | **311 ns** | **0 B** |
| `WithExceptions` | **1,405,365 ns** | **216,000 B** |

Roughly **4,500×**, for 1,000 failures. That is why
[`Result<T>`](../../src/LogiFlow.Domain/Results/Result.cs) exists.

Note what is measured: **throwing**, not `try`/`catch`. A try block that never throws costs
essentially nothing. The cost is capturing a stack trace and unwinding — which is exactly why
exceptions are fine for exceptional cases and ruinous in a validation loop.

---

## 3. The optimisations that actually matter, in order

**1. Fix the algorithm.** `List.Contains` in a loop is O(n·m). At 10,000 × 10,000 that is a
hundred million comparisons. A `HashSet` makes it O(n).

```bash
dotnet run -c Release --filter '*Lookup*'
```

**2. Fix the database.** A missing index or an N+1 dwarfs anything you do in C#. 300ms of SQL is
not fixed by saving 20ns in a loop.

**3. Stop allocating.** GC pressure is the most common .NET performance problem after the
database.

**4. Micro-optimise.** Almost never worth it. Do it only with a profiler pointing at the line.

**Amdahl's law:** if the database is 90% of your latency, making the C# infinitely fast gives you
a 10% improvement. Find the actual bottleneck first.

```
   what you fix                       typical win        how often it is the right answer
   ─────────────────────────────────────────────────────────────────────────────────────
   1  the algorithm   O(n·m) → O(n)   ████████████████   often, and it is free
   2  the database    index, N+1      ██████████████     most of the time, in this kind of app
   3  allocation      GC pressure     ██████             real, on a hot path
   4  micro-optimise  nanoseconds     ▌                  almost never, and only with a profiler
                                                         pointing at the line

   Amdahl: if SQL is 90% of your latency, infinitely fast C# buys you 10%.
```

---

## 4. Allocation

```bash
cd labs/Labs.Playground && dotnet run boxing
```

`ArrayList` boxing 100,000 ints: **4.5 MB**. `List<int>`: **1 MB**. That is why generics were
added in C# 2.0.

> **Allocation is only half the story, and it is the half everyone optimises first.** The GC
> charges you for the objects that *survive*, not for the garbage — so a million short-lived
> allocations in a request are close to free, and a cache that grows all day is not.
> `dotnet run gc` makes that visible, `dotnet run pooling` shows `ArrayPool` and its two traps,
> and [module 19](../module-19-memory-and-gc/) is the full picture.

### Where allocations hide

| Source | Fix |
|---|---|
| Boxing a value type into `object` | generics |
| `string +=` in a loop | `StringBuilder` or `string.Concat` |
| Closures capturing variables | `static` lambdas, pass state as an argument |
| LINQ in a hot path | a plain loop — **only** if measured |
| `params object[]` | `params ReadOnlySpan<T>` (C# 13) |
| `async` returning `Task` for a synchronous path | `ValueTask<T>` |

📂 `Result.FirstFailureOrSuccess(params ReadOnlySpan<Result>)` uses the span form, so the
caller's array is stack-allocated and the happy path allocates nothing.

📂 `Dispatcher` uses a `static` lambda with state passed as an argument, specifically so it
cannot capture.

### Gen0 / Gen1 / Gen2

.NET's GC is generational. Most objects die young, and collecting Gen0 is cheap. The expensive
one is **Gen2**, which pauses everything.

The implication: **short-lived allocations are relatively cheap; long-lived ones are not.** A
cache that grows without bound is worse than a million temporary strings.

```
   Gen0   ████████████████████   nearly everything dies here. Collecting it is cheap.
   Gen1   ████                   survivors of one collection
   Gen2   █                      survivors of survivors — collecting THIS pauses everything

   so: a million short-lived strings are cheaper than one cache that grows without bound,
   and allocation rate matters because it drives how often the expensive one runs.
   On a server this shows up as p99 latency spikes, never as a slower average — which is
   exactly why average latency hides it.
```

---

## 5. `Span<T>`

```bash
cd labs/Labs.Playground && dotnet run spans
```

A `Span<T>` is a pointer plus a length. Slicing copies nothing:

```csharp
ReadOnlySpan<char> field = csv.AsSpan()[..comma];   // zero allocations
string[] parts = csv.Split(',');                    // an array + N strings
```

Irrelevant once. Decisive when parsing a million rows.

**The catch:** a `Span<T>` lives on the stack, so it cannot be a field of a class, cannot be
captured by a lambda, and cannot cross an `await`. Use `Memory<T>` when you need those.

**Do not reach for spans by default.** They make code harder to read for a win that only matters
in genuinely hot paths.

---

## 6. `FrozenDictionary`

.NET 8 added it: slower to build, faster to read. It analyses the keys at construction to choose
an optimal lookup strategy.

**Exactly right for static configuration** — which is why `OrderStateMachine` and the outbox type
allow-list both use one. **Exactly wrong for anything you mutate.**

```bash
dotnet run -c Release --filter '*Dictionary*'
```

---

## 7. EF Core performance

The five that matter, in order:

1. **`AsNoTracking()` for reads.** 20–30% faster, less memory. Free.
2. **Project, do not load.** `Select` only the columns you need. A list view fetching whole
   aggregates is the most common cause of a slow endpoint.
3. **Kill N+1.** `Include`, or better, a projection. See module 06.
4. **Batch.** `GetManyAsync` with `WHERE Id IN (...)` instead of a loop of round trips. Watch the
   2,100-parameter limit.
5. **Compiled queries** for genuinely hot paths — they skip expression-tree translation on every
   call:
   ```csharp
   private static readonly Func<LogiFlowDbContext, Guid, Task<Order?>> GetById =
       EF.CompileAsyncQuery((LogiFlowDbContext db, Guid id) =>
           db.Orders.FirstOrDefault(o => o.Id == OrderId.From(id)));
   ```
   Worth it for a query called thousands of times a second; noise otherwise.

**Always look at the SQL before optimising the C#.** Turn on
`Microsoft.EntityFrameworkCore.Database.Command: Information` and read what actually ran.

---

## 8. Finding the real bottleneck

```bash
dotnet-counters monitor --process-id <pid>    # live GC, thread pool, request rate
dotnet-trace collect --process-id <pid>       # CPU profile
dotnet-dump collect --process-id <pid>        # heap snapshot for leaks
```

Then the distributed trace (module 11) tells you which span owns the time. Guessing is the step
to skip, not the profiling.

---

## 9. Exercises

1. Run every benchmark. Note which results surprise you — those are where your intuition needs
   correcting.
2. Add a benchmark comparing `AsNoTracking()` against tracked reads over 1,000 orders.
3. Add one for `GetManyAsync` versus a loop of `GetAsync` calls, at 10 and 100 ids.
4. Take the `HighValueSkus` lab solution and write a zero-allocation version. Measure it. Decide
   whether the readability cost is worth it — the answer is usually no, and being able to say so
   with numbers is the skill.

---

## 10. Golden rules

> The card.

1. **Measure, then optimise.** A `Stopwatch` in a console app measures the JIT warming up;
   BenchmarkDotNet warms up, iterates, reports variance, and stops the JIT deleting your
   benchmark.
2. **`Allocated` usually matters more than `Mean`.** A method 20 ns slower that allocates nothing
   beats a faster one that produces garbage, because GC pauses hit every request.
3. **Fix the algorithm, then the database, then allocation, and only then the microseconds.**
   Amdahl decides how much any of it is worth.
4. **A `List.Contains` inside a loop is O(n·m).** At 10,000 × 10,000 that is a hundred million
   comparisons a `HashSet` would not do.
5. **Short-lived allocations are cheap; long-lived ones are not.** Gen0 is nearly free; Gen2
   pauses everything.
6. **Exceptions cost about 4,500× a returned value here** — and the cost is in the throw, not the
   `try`. That is why business outcomes are `Result`s.
7. **`Span<T>` only in a measured hot path.** It cannot be a field, cannot be captured, cannot
   cross an `await`, and it makes the code harder to read.
8. **`FrozenDictionary` for data that never changes**, and never for anything you mutate.
9. **For EF Core, in this order: `AsNoTracking`, project instead of loading, kill N+1, batch
   lookups, and only then compiled queries.**
10. **Read the SQL before optimising the C#.** 300 ms of database is not fixed by saving 20 ns in
    a loop.
11. **Profile; do not guess.** `dotnet-counters`, `dotnet-trace`, the distributed trace, the
    query plan. Guessing is the step to skip.

---

## 11. Interview questions

**"How do you find a performance problem?"**
Measure first. Metrics to confirm and scope it, a trace to localise it, a profiler if it is CPU,
the SQL log if it is the database. Then fix the biggest contributor. Say "I would not guess" —
that is the answer.

**"Why is allocation a problem if the GC is fast?"**
Gen0 collection is cheap, but allocation rate drives collection frequency, and Gen2 collections
pause everything. On a server that shows up as p99 latency spikes rather than a slower average —
which is why average latency hides the problem.

**"When would you use `Span<T>`?"**
Parsing or slicing in a hot path where the allocations are measurable. Not by default — it cannot
be stored in a field, captured by a lambda, or used across an `await`, and it makes code harder
to read.

**"Is LINQ slow?"**
Slightly, in-memory: delegate invocation and enumerator allocation per element. Almost never the
bottleneck, and readability usually wins. With `IQueryable` it is a different question entirely —
there the risk is a query that cannot translate and silently evaluates on the client.

**"How do you speed up an EF Core query?"**
Read the generated SQL first. Then: `AsNoTracking` for reads, project instead of loading whole
entities, eliminate N+1, batch lookups, and only then consider compiled queries or raw SQL.

---

## Next

→ [Module 15 — ASP.NET Core in depth](../module-15-aspnetcore-in-depth/)

That is the end of Part III, and of the application itself. Part IV steps back from it: the
framework you have been using (15), the map of how C#, LINQ, SQL and ASP.NET Core actually relate
(16), the market this was all for (17), and a UI over the top (18).

If this module left you wanting the layer below — where the allocations actually go and why a
collection costs what it costs — that is [module 19](../module-19-memory-and-gc/), and it can be
read now rather than later.
