# The LogiFlow Course

Twenty-nine modules that take you from advanced C# through the runtime underneath it, out to a
deployable enterprise .NET service — and then to a job offer. Every module points at real,
running, tested code in this repository; nothing here is a toy example written to illustrate a
point and then thrown away.

**Prerequisite:** you can already write C#. You know what a class, an interface and a `List<T>`
are. This course is about everything after that.

> **Start with [Module 16 — The layer map](module-16-the-layer-map/).** It is ten minutes, it is
> deliberately out of order, and it answers the question underneath most confusion about this
> stack: how C#, SQL, LINQ and ASP.NET Core relate to each other, and where one stops and the
> next begins. Read it now for the map, and again after module 15 for the meaning.

---

## How each module works

Each has the same five parts:

1. **The idea** — what the concept is, and what problem it exists to solve.
2. **In this codebase** — the exact files that use it. Read them; the comments carry the detail.
3. **Do it** — an exercise, a lab, or a deliberate breakage to observe.
4. **Golden rules** — the card. The module compressed into the dozen sentences worth carrying
   around, each one only useful when you can say *why* in the next breath.
5. **Interview questions** — what you will actually be asked, with the answer sketched.

Sixteen of the modules also have **deeper chapters** — fifty focused files sitting next to the
README, one per topic, listed in a contents table at the top of the module. The README is the
summary; a chapter is the full treatment, with the code, the traps and the interview answer. They
are also what the `Covered in:` comments throughout `src/` and `tests/` point at, so reading a
class and reading its chapter are one gesture:

```
course/module-06-efcore/
  README.md                            the summary, and the contents table
  01-dbcontext-and-change-tracking.md  ← src/.../LogiFlowDbContext.cs points here
  04-n-plus-one.md
  07-outbox-pattern.md                 ← src/.../Outbox/OutboxMessage.cs points here
  …
```

Two pages collect all of it:

- **[`GOLDEN-RULES.md`](GOLDEN-RULES.md)** — every module's card, in module order. The thing to
  read the week before an interview.
- **[`LAWS-OF-CSHARP.md`](LAWS-OF-CSHARP.md)** — the same knowledge reorganised by *concept* into
  twelve books, so that when something surprises you it is findable by what it is about. Start
  with "the ten that are really one law".

Work them in order. Later modules assume earlier ones.

```
        ┌────────────────────────────────────────────────────────────────────────┐
        │   16  THE LAYER MAP — ten minutes, and read it before anything else    │
        └────────────────────────────────┬───────────────────────────────────────┘
                                         ▼
   I     00 ─► 01 ─► 02 ─► 03 ─► 04           the language
   II    05 ─► 06 ─► 07 ─► 08 ─► 09           the architecture  ← 07 is the one that gets you hired
   III   10 ─► 11 ─► 12 ─► 13 ─► 14           production
   IV    15 ─► 16 ─► 17 ─► 18                 the framework · the map again · the market · the UI
                    ▲
                    └── the second read of 16. Same file, and now it means something.

   V     19 ─► 20 ─► 21 ─► 22                 UNDER THE LANGUAGE — memory, equality,
                                              threads, the CLR itself
   VI    23 ─► 24 ─► 25 ─► 26 ─► 27           the things that break in production,
                                              the design vocabulary, and the language history
```

Parts V and VI are the difference between passing an interview and being trusted with the
service afterwards. They can be read after Part I if you are impatient — 19 and 21 in particular
follow directly from modules 01 and 04.

---

## The modules

### Part I — The language

| # | Module | You will understand |
|---|---|---|
| 00 | [Setup and tooling](module-00-setup/) | The SDK, the build system, central package management, why the build breaks on a warning |
| 01 | [Advanced C#](module-01-csharp-advanced/) | Records, structs vs classes, pattern matching, nullable reference types, generics, static abstract members |
| 02 | [Delegates, lambdas and closures](module-02-delegates-and-closures/) | What a lambda compiles into, closure capture, the `for`-loop trap, `Func` vs `Expression` |
| 03 | [LINQ internals](module-03-linq-internals/) | Deferred execution, `yield return`, iterators, expression trees, writing your own operators |
| 04 | [Async and concurrency](module-04-async/) | What `await` really does, the state machine, `ConfigureAwait`, cancellation, deadlocks, `Channel<T>` |

### Part II — The architecture

| # | Module | You will understand |
|---|---|---|
| 05 | [Clean Architecture and DDD](module-05-clean-architecture/) | Entities vs value objects, aggregates, domain events, `Result` vs exceptions, state machines |
| 06 | [EF Core in depth](module-06-efcore/) | Change tracking, fluent mapping, value conversions, N+1, the specification pattern, interceptors, the outbox |
| 07 | [SQL, transactions and concurrency](module-07-sql-and-transactions/) | Indexes and execution plans, isolation levels, optimistic concurrency, pagination that does not lie |
| 08 | [CQRS](module-08-cqrs/) | Commands vs queries, vertical slices, building a mediator, read models, pipeline behaviours |
| 09 | [Advanced LINQ](module-09-advanced-linq/) | Grouping, joins, aggregation, dynamic predicates — and the SQL each one generates |

### Part III — Production

| # | Module | You will understand |
|---|---|---|
| 10 | [Cross-cutting concerns](module-10-cross-cutting/) | DI lifetimes and captive dependencies, minimal APIs, caching, error handling, rate limiting, transactional email |
| 11 | [Observability](module-11-observability/) | Structured logging, OpenTelemetry, traces vs metrics vs logs, health checks that mean something |
| 12 | [Testing](module-12-testing/) | The pyramid, testing handlers, integration tests, architecture tests, eventual consistency |
| 13 | [Deployment](module-13-deployment/) | Configuration and secrets, supply-chain security, migrations in CI, containerising a .NET app |
| 14 | [Performance](module-14-performance/) | Benchmarking properly, allocation, `Span<T>`, compiled queries, finding the actual bottleneck |

### Part IV — The framework, the map, the UI, and the job

| # | Module | You will understand |
|---|---|---|
| 15 | [ASP.NET Core in depth](module-15-aspnetcore-in-depth/) | The host, how the middleware pipeline is really built, two-phase routing, parameter binding, endpoint filters vs middleware vs behaviours, the Options pattern, authn vs authz, Kestrel behind a proxy |
| 16 | [The layer map](module-16-the-layer-map/) | **How C#, SQL, LINQ and ASP.NET Core differ and fit together** — one request through all four, the exact border where LINQ becomes SQL, and where each layer's bugs live |
| 17 | [Landing a .NET job in Modena and Bologna](module-17-career-emilia-romagna/) | How the Emilia-Romagna market is shaped, what it hires for, contracts and RAL, the Italian CV, the domain vocabulary, and a twelve-week plan |
| 18 | [Blazor: a UI over the API](module-18-blazor/) | Render modes and the circuit, the two lifecycle bugs everyone hits, typed clients and delegating handlers, letting the server own the state machine, loading states and `@key` diffing |

### Part V — Under the language

> The runtime your C# actually runs on. Every module here is observable — each one has runnable
> demos that print the behaviour rather than asserting it.

| # | Module | You will understand |
|---|---|---|
| 19 | [Memory, the heap, and the GC](module-19-memory-and-gc/) | Stack vs heap properly, generations and the write barrier, the LOH, what a leak actually is in a managed language, `IDisposable`, `ArrayPool`, and how to diagnose a service that grows all day |
| 20 | [Equality, hashing and collections](module-20-equality-and-collections/) | The five kinds of equality, the `GetHashCode` contract and the mutable-key bug, inside `Dictionary<K,V>`, and choosing a collection so an O(n) loop does not become O(n²) |
| 21 | [Threading and the memory model](module-21-threading-and-memory-model/) | Threads vs tasks vs cores, lost updates, `Interlocked` and lock discipline, **the memory model** — why another thread may never see your write — thread-pool starvation, and the deadlock reproduced live |
| 22 | [Inside the CLR](module-22-clr-internals/) | C# → IL → JIT → machine code, tiered compilation and OSR, ReadyToRun and Native AOT, method tables, how generics are really instantiated, what reflection actually costs, and source generators |

### Part VI — What breaks in production, and how to talk about it

| # | Module | You will understand |
|---|---|---|
| 23 | [Text, culture, time and serialization](module-23-text-culture-serialization/) | Why `"👍".Length` is 2, the three-way culture rule, why an Italian machine reads `1234.5` as `12345`, DST and `TimeProvider`, and the four `System.Text.Json` traps |
| 24 | [Security for a .NET API](module-24-security/) | The authn/authz middleware order, what a JWT is and is not, password hashing, why parameterisation works, over-posting, IDOR, and the headers that matter |
| 25 | [Distributed systems and integration](module-25-distributed-systems/) | The dual-write problem, at-least-once and idempotency, retries with backoff and jitter, circuit breakers and bulkheads, sagas and compensation, and eventual consistency explained honestly |
| 26 | [Design patterns and SOLID](module-26-patterns-and-solid/) | The patterns actually in this codebase and the problem each one solved — plus the ones deliberately left out, and how to argue both sides of the repository pattern |
| 27 | [C# version by version](module-27-csharp-versions/) | Every release from 1.0 to 14, what each one was solving, and which features you lose on .NET 8 and .NET 6 |
| 28 | [Industrial software and the IT/OT boundary](module-28-industrial-and-ot/) | MES, SCADA, WCS and OT as words you can use precisely, the machine layer's missing schema, subscription over polling, OEE as a definition rather than a measurement, traceability, AGV traffic deadlock, and what `cantiere` and `trasferta` mean for your life |

---

## Suggested pace

| If you have… | Do this |
|---|---|
| **2 weeks, full time** | One module per half-day. All labs, all demos. |
| **6 weeks, evenings** | Two or three modules a week. All labs. |
| **A job interview on Friday** | Modules **16**, 03, 06, 07 and **15**, plus **20** and **21** — then [`LAWS-OF-CSHARP.md`](LAWS-OF-CSHARP.md) on the train, stopping at every law you cannot justify. |
| **You already know .NET** | Skip to Part II. Do 06 and 07 properly — that is where seniority shows. Then **Part V**, which is what most .NET developers with five years never learned. |
| **You are job-hunting in Italy** | Read [module 17](module-17-career-emilia-romagna/) first and work backwards from what it says the market tests. |
| **You want something to show** | Modules 05–07, then **18** — build the Blazor UI and put the whole thing on GitHub. |
| **You want to be genuinely senior** | 19, 21 and 22, and then be able to explain the `gc`, `race` and `deadlock` demos to someone else. That is the actual test. |

---

## The one rule

**Run everything.** Reading that LINQ is lazily evaluated teaches you a sentence. Watching
`...evaluating 3` print *after* the line that said the query was built teaches you the concept:

```bash
cd labs/Labs.Playground
dotnet run list          # 34 demos, grouped by topic
dotnet run deferred      # run one
dotnet run all           # run every one, in order
```

The demos are the spine of the course. A few worth running today, whatever module you are on:

```bash
dotnet run race          # loses ~600,000 of 800,000 increments. A race is not a rare event.
dotnet run leaks         # a memory leak in a garbage-collected language, proven with WeakReference
dotnet run deadlock      # the classic async deadlock, reproduced safely and explained
dotnet run culture       # why an Italian machine reads "1234.5" as 12345, silently
dotnet run hashcode      # a dictionary entry that is present, unfindable and unremovable
```

The same goes for the performance claims. This course asserts that exceptions are thousands of
times more expensive than a returned value. Do not take that on trust — it is measurable:

```bash
cd labs/Labs.Benchmarks && dotnet run -c Release --filter '*ErrorHandling*'
```

On the machine this course was written on, that prints **1,405,365 ns and 216,000 bytes** for
the exception version against **311 ns and 0 bytes** for the `Result` version. Roughly 4,500×.
Now you know it rather than believe it.

> One caveat the demos state themselves: **timings from a console app measure the JIT.**
> Allocation counts are exact and reliable; elapsed time needs BenchmarkDotNet. Two demos
> (`valuetask` and `reflection`) ask you to run them with `-c Release` for this reason.

---

## A note on the test suites

```bash
dotnet test LogiFlow.slnf        # everything that should pass: 107 tests
dotnet test labs/Labs.Exercises  # your homework: 113 tests, deliberately RED
```

Running `dotnet test` with no argument runs both, so it reports failures until you have worked
through the labs. That is the intended state — the exercises start red and you make them green.

The filter is "everything that should be green", not "the logistics application": it now also
covers `LogiFlow.Academy.Api`, the accounts service behind the `site/` tutorial.

| Suite | Tests | Needs |
|---|---|---|
| `LogiFlow.Domain.Tests` | 30 | nothing |
| `LogiFlow.Application.Tests` | 8 | nothing |
| `LogiFlow.ArchitectureTests` | 8 | nothing |
| `LogiFlow.Academy.Api.Tests` | 53 | nothing |
| `LogiFlow.Api.IntegrationTests` | 8 | **SQL Server** — it creates a throwaway database on it |

So 99 of the 107 run anywhere the SDK is installed. The last 8 need a SQL Server they can reach —
by default `localhost` with Windows authentication, or wherever `LOGIFLOW_TEST_SQL` points. Each run
creates its own `LogiFlow_Test_{guid}` database and drops it afterwards, so they never touch your
development data. Module 12 section 4 explains why those eight are worth the trouble anyway.

| Lab | Tests | What it makes you get right |
|---|---|---|
| 02 — LINQ | 14 | operator order, grouping, the `Max` of an empty sequence, and when to stop being clever |
| 03 — Async | 17 | a timeout that does not leak a timer, retry that preserves the stack trace, bounded concurrency that does not deadlock on failure, and collecting *every* error |
| 04 — Equality | 16 | the hashing contract, a comparer that does not overflow, O(n) instead of O(n²), and the mutable-key bug you have to predict before you run it |
| 05 — Delegates | 23 | writing `Where` yourself, the two-method iterator pattern, a closure that caches, the `for`-loop capture bug, and an event that cannot be raised or wiped from outside |
| 06 — Disposal | 18 | `Dispose` that is safe to call twice, `await using` that flushes when the body throws, a counter that survives 200 000 concurrent increments, and one loader instead of fifty |
| 07 — Specifications | 25 | composing predicates that EF Core can still translate, a cursor that does not lose precision, and pagination that does not repeat a row when somebody inserts one |

---

## Where the answers are

Every module's card, on one page: [`GOLDEN-RULES.md`](GOLDEN-RULES.md).

The whole language as one numbered canon: [`LAWS-OF-CSHARP.md`](LAWS-OF-CSHARP.md).

Lab solutions with commentary: [`SOLUTIONS.md`](SOLUTIONS.md).

Use them the way you would use a worked example — after a genuine attempt, and read the
reasoning rather than just the code. An exercise you copy teaches you nothing; one you struggle
with for twenty minutes and then compare against teaches you the shape of the problem.
