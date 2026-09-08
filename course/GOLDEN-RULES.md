# Golden rules — the whole course on one page

Every module ends with a **Golden rules** card. This file is all of them, in order, with a link
back to the module each one comes from. It is not a summary of the course: it is the set of
sentences worth having ready, and each one is only ready when you can say *why* in the next
breath. The module is where the why lives.

**How to use it.** Read a module, then its card. The week before an interview, read only this
page and stop at every rule you cannot justify in two sentences — that is your revision list,
and it is usually four or five rules, not three hundred.

> **Stopping honestly is the hard part**, because a rule you agree with reads exactly like a
> rule you could explain. `site/viva.html` does the stopping for you: it shows the claim,
> hides the justification until you have written or said yours, and then schedules whatever
> you fumbled. The deck is generated from this file — after editing a card here, run
> `dotnet run tools/viva-deck.cs`.

> **Organised by concept instead?** [`LAWS-OF-CSHARP.md`](LAWS-OF-CSHARP.md) is the same knowledge
> in twelve books — types, equality, null, memory, laziness, async, the C#/SQL border, errors,
> culture, boundaries, performance, design. Use this page to revise a module; use that one when
> something has surprised you and you want to look up *what kind of thing* it was.

```
   ┌─ ASP.NET Core ─ HTTP, routing, binding, auth, status codes ──────────┐   modules 10, 15, 24
   ├─ C# ─────────── your rules, in a layer that knows nothing else ──────┤   modules 01–05, 26
   ├─ LINQ + EF ──── THE TRANSLATOR. This line is where most bugs live ───┤   modules 03, 06, 09
   └─ SQL Server ─── plans, indexes, locks, durability, arbitration ──────┘   module 07
     ══════════════════════════════════════════════════════════════════        the map: module 16
   ┌─ THE CLR ────── memory, threads, the JIT. Underneath all four. ──────┐   modules 19–22
   └──────────────────────────────────────────────────────────────────────┘
```

---

## The twelve that decide interviews

If you only rehearse twelve, rehearse these. Each links to the module that earns it.

1. **`IEnumerable` runs here; `IQueryable` runs there.** Assigning one to the other silently moves
   the filtering from the database into your process. — [03](module-03-linq-internals/) ·
   [16](module-16-the-layer-map/)
2. **A LINQ query is a recipe, not a result.** Compose while it is a query, materialise once.
   — [03](module-03-linq-internals/)
3. **N+1 is one query for the parents and one per child collection.** Fix with a projection, or
   `Include`; lazy loading causes it invisibly. — [06](module-06-efcore/)
4. **Equality columns first, then range and sort columns** — and never wrap a column in a
   function. — [07](module-07-sql-and-transactions/)
5. **Two users, one row: `rowversion`, 409, retry.** Then a CHECK constraint, because the database
   is the only guard every writer passes. — [07](module-07-sql-and-transactions/)
6. **`async` does not create a thread; it frees one.** Never `.Result`, always pass the token.
   — [04](module-04-async/)
7. **A singleton must never hold a scoped service.** Scope validation catches it in Development
   and is off in Production. — [10](module-10-cross-cutting/) · [15](module-15-aspnetcore-in-depth/)
8. **The middleware pipeline is one nested function built in reverse.** Order is behaviour;
   authorization sits between matching and executing. — [15](module-15-aspnetcore-in-depth/)
9. **An aggregate is a consistency boundary; reference other aggregates by id.**
   — [05](module-05-clean-architecture/)
10. **Expected outcomes are `Result`s; bugs and infrastructure throw.**
    — [05](module-05-clean-architecture/)
11. **The in-memory provider is not a database.** No constraints, no transactions, no
    `rowversion` — it passes tests production fails. — [12](module-12-testing/)
12. **Measure before you optimise, and read the SQL before you touch the C#.**
    — [14](module-14-performance/)

---

## The six that separate a senior candidate

The twelve above get you through the interview. These are the ones that, offered unprompted, make
the interviewer change how they are talking to you — because almost nobody with five years brings
them up.

1. **The GC charges you for survivors, not for garbage.** A million short-lived objects are cheap;
   ten thousand that survive are not — so a leak is a reference you forgot, never memory you forgot
   to free. — [19](module-19-memory-and-gc/)
2. **The hash of a key must not change while it is in the table.** Mutate it and the entry is
   present, unfindable and unremovable — which is why dictionary keys are immutable.
   — [20](module-20-equality-and-collections/)
3. **Without a fence, another thread may never see your write.** The compiler can hoist it into a
   register and the CPU can reorder it; `lock`, `Interlocked` and `volatile` are the fences — and
   `volatile` still does not make `++` atomic. — [21](module-21-threading-and-memory-model/)
4. **Reflection is slow because of the metadata lookup and the boxing, not because it is
   reflection** — so cache the `MemberInfo`, build a delegate, or better, move the work to a source
   generator at compile time. — [22](module-22-clr-internals/)
5. **An unqualified `ToString()` or `Parse` is a latent bug on a non-English machine.** On `it-IT`,
   `"1234.5"` parses to `12345`, silently. Ordinal for identifiers, Invariant for persistence,
   Current for humans. — [23](module-23-text-culture-serialization/)
6. **You cannot atomically write to two systems, and a timeout tells you nothing about whether the
   work happened.** Hence the outbox, at-least-once delivery, and idempotent consumers.
   — [25](module-25-distributed-systems/)

---

## [Module 00 — Setup and tooling](module-00-setup/)

> The card. Reread it before you touch anyone else's build.

1. **Warnings are errors in `src/`.** A warning nobody reads is a warning that does nothing — and
   the one it caught here was a real CVE.
2. **One version per package, declared once.** Central package management turns "which project is
   on which version?" into a question nobody has to ask.
3. **Pin the SDK.** `global.json` is the difference between "works on my machine" being a joke and
   being a bug report.
4. **Newest is not the goal. Patched and compatible is.** Read the advisory, then pick the version
   on the major line your framework was built against.
5. **Scope an exception, never turn a rule off globally.** Generated code gets its own
   `.editorconfig` section; the code you own keeps the rules.
6. **Turn the SQL log on and leave it on** for modules 06, 07 and 09. Reading the generated SQL is
   the fastest way to learn what your LINQ costs.
7. **Check capability, not environment.** "Is a collector configured?" keeps working when someone
   runs one locally; "is this Development?" does not. It is why Redis and OTLP are optional here.

## [Module 01 — Advanced C#](module-01-csharp-advanced/)

> The card. These are the sentences that come out under interview pressure.

1. **Record for values, class for identity, struct for small immutable data.** `readonly record
   struct` is all three properties at once, and it is what a strongly-typed ID should be.
2. **A struct larger than ~16 bytes costs more than an allocation**, because it is copied on every
   assignment, argument pass and return.
3. **`readonly` on a struct is not decoration.** It is what stops the compiler making a defensive
   copy on every member access through a readonly field or an `in` parameter.
4. **Nullable reference types are compile-time only.** Nothing is checked at runtime, so you still
   validate at every boundary: JSON, the database, an unannotated library.
5. **A `switch` expression over an enum with no `default` arm is a tripwire.** Add a member without
   handling it and the build fails. Adding `default => …` throws that away.
6. **Enum for a closed set with no data; smart enum the moment a member carries behaviour or a
   value** — like JPY having zero decimal places.
7. **Pin the numeric values of any enum you persist.** Insert a member alphabetically without them
   and every existing row silently changes meaning.
8. **Static abstract interface members cannot appear in an expression tree.** When generic math or
   a static factory meets EF Core or a mocking library, build the tree by hand with
   `Expression.New`.
9. **Throw for a developer mistake, return a value for a user's.** `Money + Money` in the wrong
   currency is a wiring bug, not a business outcome — so it throws.

## [Module 02 — Delegates, lambdas and closures](module-02-delegates-and-closures/)

> The card.

1. **A lambda captures the variable, not its value.** It is read when the delegate runs, not when
   it was written.
2. **In a `for` loop, copy the loop variable into a local before capturing it.** `foreach` was
   fixed in C# 5; `for` was not, and that inconsistency is why the bug survives.
3. **Capturing allocates.** A capture-free lambda is cached and allocates once per process; one
   that captures allocates a display class every time it is created.
4. **Use `static` lambdas on hot paths** and pass state as an argument — capture becomes a compile
   error instead of a silent allocation.
5. **`Func` is code you can only run. `Expression<Func>` is data describing that code.** Identical
   syntax; the target type decides which the compiler emits.
6. **Never hand a compiled `Func` to a query provider.** EF Core cannot see inside a delegate, so
   it fetches every row and filters in memory.
7. **Combine predicates by rewriting the parameter node with an `ExpressionVisitor`**, then
   `Expression.AndAlso`. `a.Compile()(x) && b.Compile()(x)` compiles and then throws at query time.
8. **Expression trees are immutable.** A visitor returns a new tree; it does not edit the old one.
9. **Every `+=` on a long-lived event needs a `-=`.** A publisher holding a delegate to a dead
   subscriber is the most common managed memory leak in .NET.

## [Module 03 — LINQ internals](module-03-linq-internals/)

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

## [Module 04 — Async and concurrency](module-04-async/)

> The card. Most production async bugs are one of these nine.

1. **`async` does not create a thread. It frees one.** A server handling 10,000 idle connections
   needs roughly zero threads, not 10,000.
2. **Async all the way down.** `.Result`, `.Wait()` and `.GetAwaiter().GetResult()` deadlock where
   there is a synchronisation context and starve the thread pool where there is not — which fails
   under load instead of immediately, so you ship it.
3. **`async void` only for a literal event handler.** Anywhere else the exception has no `Task` to
   land in and takes the process down.
4. **Pass the `CancellationToken`, always, all the way to the driver.** A user who closed their
   browser should stop costing you a connection.
5. **A cancelled request is 499, not 500.** Logging the client's departure as your failure
   pollutes the error budget you actually need.
6. **`ConfigureAwait(false)` in library code; unnecessary in ASP.NET Core application code**,
   which has no synchronisation context to capture.
7. **`await` is sequential.** To run two things at once, start both, then `await Task.WhenAll` —
   and remember it surfaces only the first exception.
8. **`DbContext` is not thread-safe.** Concurrent queries need a context each, from
   `IDbContextFactory`.
9. **A `BackgroundService` is a singleton.** No scoped services in its constructor: take
   `IServiceScopeFactory` and open a scope per iteration, or you have built a memory leak that
   also serves stale data.

## [Module 05 — Clean Architecture and Domain-Driven Design](module-05-clean-architecture/)

> The card. This module is what interviewers mean when they say "architecture".

1. **Dependencies point inward, and a test says so.** The Domain project has no package and no
   project references; `LayeringTests` keeps it that way after you have stopped watching.
2. **Inner layers declare what they need; outer layers supply it.** That is dependency inversion —
   not "we use a DI container", which is a different thing people routinely confuse it with.
3. **Identity means entity. Attributes mean value object.** Two customers with the same name are
   two customers; two addresses with the same fields are one address.
4. **An aggregate is a consistency boundary, not a folder.** Everything inside it is saved in one
   transaction, and its invariants are true at the end of every one.
5. **Reference other aggregates by id.** An object reference is an invitation to mutate two
   aggregates in one transaction — which is why the mapping here withholds the navigation.
6. **Keep aggregates small.** If two things need not be consistent *immediately*, they belong
   apart and an event reconciles them.
7. **Expose read-only collections.** A public `List<T>` bypasses every rule in `AddLine` and every
   domain event, and the aggregate becomes decoration.
8. **If a human could reasonably cause it, return a `Result`. If only a bug or the infrastructure
   could, throw.** Roughly 4,500x cheaper here — but the real win is that failure is in the
   signature and the compiler makes callers acknowledge it.
9. **Clients branch on a stable `Code`, never on a message.** The message is free to change and to
   be translated.
10. **Events are facts, in the past tense.** `OrderPlacedDomainEvent`, not `PlaceOrderEvent`. If
    you want to reject one, you modelled a command.
11. **Keep the state machine as data, in one table.** A guard clause at the top of eight methods
    means someone eventually updates seven of them.
12. **Never `double` for money.** `decimal` is base-10 and stores 0.1 exactly; binary floating
    point cannot.

## [Module 06 — EF Core in depth](module-06-efcore/)

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

## [Module 07 — SQL, transactions and concurrency](module-07-sql-and-transactions/)

> The card. This is the module that gets people hired in this market — module 17 says why.

1. **Equality columns first, then range and sort columns.** The leftmost-prefix rule decides
   whether your index gets used at all.
2. **A function around a column kills the seek.** Compare the column raw; normalise the
   *parameter* instead.
3. **In SQL Server, several NULLs collide in a unique index.** Nullable uniqueness needs a
   filtered index — `WHERE [Col] IS NOT NULL`.
4. **`WITH (NOLOCK)` is Read Uncommitted, not a performance switch.** It can read a row twice,
   skip it entirely, or return data that was rolled back. If you need non-blocking reads, use
   snapshot isolation.
5. **Optimistic concurrency for web traffic.** No lock survives a stateless HTTP request: detect
   the clash with `rowversion`, return 409, retry.
6. **A real guarantee needs three layers** — the aggregate for a friendly error, `rowversion` for
   the race, and a CHECK constraint because the database is the only guard that every writer
   passes, including a bad migration and a DBA at 2am.
7. **`COUNT` before `Skip`/`Take`**, or every client is told it is on page 1 of 1.
8. **Every paginated `ORDER BY` needs a unique tiebreaker.** Without one, `OFFSET/FETCH` can show
   a row on two pages and another on none. Almost every paginated endpoint in the wild has this
   bug.
9. **Keyset for infinite scroll, offset for page numbers.** Offset gets slower with depth because
   the server produces and discards every preceding row.
10. **`SELECT MAX(id) + 1` is a race.** Use a sequence — and know that a sequence is not gapless,
    which matters where invoice numbering is a legal requirement.
11. **With `EnableRetryOnFailure`, hand the whole transaction to `IExecutionStrategy`** — and make
    that block idempotent, because it may genuinely run twice.
12. **Look at the execution plan before changing anything.** Seek is good, scan on a large table
    is not, and a key lookup means you are one `INCLUDE` away from a covering index.
13. **A view is a saved `SELECT`, not saved data.** It costs what the full query costs; what it
    buys is a stable contract and a permission boundary.
14. **A scalar UDF runs once per row and lies in the plan.** The optimiser cannot see inside it, so
    it costs the call at nearly nothing — the one place this module's "trust the plan" advice
    breaks. Use an inline table-valued function instead.
15. **EF's raw-SQL helpers are composable query builders.** That single fact explains both why
    `NEXT VALUE FOR` fails inside `SqlQuery<T>` and why you cannot put a `Where` after an `EXEC`.

## [Module 08 — CQRS and the mediator pattern](module-08-cqrs/)

> The card.

1. **Commands change state and return as little as possible; queries change nothing and return a
   purpose-built DTO.** Naming follows: imperative for one, interrogative for the other.
2. **CQRS is not two databases, not event sourcing, not microservices.** It is two models. Being
   able to say that is half the interview question.
3. **Never load aggregates to render a list.** Twenty-five aggregates with all their children,
   registered with the change tracker, to display six columns, is why people conclude that Clean
   Architecture is slow.
4. **Everything that changes together lives together.** Command, validator and handler in one
   file; retiring a feature is deleting a file, not archaeology.
5. **Pipeline order is behaviour, not style.** Logging outermost so its timing covers everything;
   validation before caching; caching before the transaction; the transaction innermost so it is
   held for the shortest possible time.
6. **Validators check shape; the domain checks state.** A validator that queries the database has
   become a handler — and its answer is stale by the time the handler runs anyway.
7. **Cache by opt-in, never automatically.** A behaviour that cached everything would eventually
   cache something that must be fresh, and that bug is invisible until a customer sees it.
8. **A cache key must contain every parameter that changes the answer** — including the tenant or
   user when the result is scoped to a caller. Leaking one customer's data to another through a
   shared key is a regularly-shipped security bug.
9. **Some things are never cached.** Stock levels, above all: stale stock means orders you cannot
   fulfil.
10. **Handlers stay `internal`.** Nothing outside the Application layer should be able to call one
    directly and skip the pipeline.

## [Module 09 — Advanced LINQ](module-09-advanced-linq/)

> The card.

1. **Aggregate over many rows in SQL; apply business rules to the few rows you fetched, in C#.**
   The database is better at the first and has no idea about the second.
2. **In SQL, a group is its key and its aggregates. The rows are gone.** Asking for the members
   throws, and that is EF Core 3.0+ protecting you.
3. **Cast to nullable before `Sum`.** SQL's `SUM` over zero rows is `NULL`, not `0`:
   `Sum(x => (decimal?)x.Amount) ?? 0m`.
4. **A `Where` before `GroupBy` is a `WHERE`; after the aggregate projection it is a `HAVING`.**
   Two different questions that look almost identical in C#.
5. **Count the distinct thing you mean.** Counting lines when you meant orders produces a
   plausible wrong number, which is the worst kind of reporting bug.
6. **Six aggregates over one grouping cost one scan.** Six queries for six numbers is the mistake
   this avoids.
7. **Grouping on a computed key cannot use an index on the underlying column.** Fine for a report,
   wrong for a hot path.
8. **A predicate comparing two columns can never seek.** If it matters, add a persisted computed
   column and index that.
9. **Compose filters conditionally; never write `WHERE (@x IS NULL OR Col = @x)`.** One cached
   plan for every combination is usually a bad plan for all of them.
10. **Never accept a sort or filter column as free text.** A `switch` over an enum is compile-time
    checked, cannot be handed an arbitrary column name, and fails the build when you add a field
    and forget it.
11. **Read the generated SQL for every report query you write.** Reports are where a single
    careless operator turns into a table scan nobody notices for a year.

## [Module 10 — Cross-cutting concerns](module-10-cross-cutting/)

> The card. This is where the subtle production bugs live.

1. **Transient per resolution, scoped per request, singleton per process.** `DbContext` is scoped
   because it is a unit of work and is not thread-safe.
2. **A singleton must never hold a scoped service.** If it needs one, inject
   `IServiceScopeFactory` and open a scope per unit of work.
3. **Each layer registers itself.** Otherwise `Program.cs` becomes a 300-line permanent merge
   conflict.
4. **An endpoint binds, dispatches and maps the result.** An `if` about business state inside an
   endpoint is a smell.
5. **A non-nullable property is a required parameter.** `[AsParameters]` never sees your property
   initialiser — make optional query parameters nullable and resolve the default yourself.
6. **POST to create; POST a verb sub-resource for a state transition; PATCH for a partial
   update** — and a 201 always carries a `Location` header.
7. **Errors are RFC 9457 Problem Details with a stable `code`.** Clients branch on the code, never
   on the human-readable detail.
8. **401 is "I do not know who you are"; 403 is "I know, and no".** 400 is malformed; 409 is
   well-formed and conflicting. An empty search result is 200 with `[]`.
9. **A binding failure is the caller's 400, not your 500.** Otherwise your error alerting fills up
   with other people's typos.
10. **Never return a stack trace.** The detail goes to the log; the client gets a `traceId`.
11. **A cache failure must degrade, never fail.** Redis down means a cache miss and a slower
    response, not an error page.
12. **Rate limit before authentication, authenticate before authorise, and register exception
    handling first.** Middleware order is behaviour, not style.
13. **`ValidateOnStart` on everything the application cannot run without.** A missing signing key
    should stop the process from starting, not produce 500s an hour later.
14. **Queue email inside the transaction; deliver it outside.** Sending during a transaction holds
    locks on a mail server's schedule and mails customers about orders that then roll back.
15. **At-least-once delivery means the message needs an idempotency key derived from the fact** —
    never from `Guid.NewGuid()`, which is the same as having no key.
16. **Claim queue rows atomically (`UPDLOCK, READPAST`, `OUTPUT`) and count the attempt when you
    claim**, or two instances send everything twice and a poison message is immortal.
17. **Retry 4xx, never 5xx, exponentially with jitter, then dead-letter and keep it.** A cleanup
    job that deletes the evidence of its own failures is worse than none.
18. **Redirect all outbound mail in every non-production environment.** The incident this prevents
    happens somewhere every month and is always found by the customers.

## [Module 11 — Observability](module-11-observability/)

> The card. Almost nobody learns this properly, which is exactly why it is a differentiator.

1. **Metrics to detect, traces to localise, logs to diagnose.** Using logs for metrics is slow and
   expensive; using metrics for diagnosis is impossible.
2. **Log a message template plus named properties, never an interpolated string.** The first
   arrives as indexed fields you can group by; the second is a million unique strings.
3. **Use scopes for correlation.** One id on every line written anywhere inside the request,
   including deep inside EF Core.
4. **A rejected order is a Warning, not an Error.** Logging business rejections at Error is how a
   team learns to ignore its own error log, and alert fatigue is an outage waiting to happen.
5. **Never log secrets, and guard `EnableSensitiveDataLogging` with an environment check** — not
   with an intention to remember. It logs parameter values, which under GDPR includes names,
   emails and addresses.
6. **Instrument with OpenTelemetry so the vendor becomes a configuration change**, not a rewrite.
7. **Liveness must not check dependencies. Readiness must.** Backwards, and a brief database
   problem becomes a restart storm.
8. **Alert on symptoms users feel** — error rate, p99 latency, a business metric flatlining — not
   on CPU or memory. Latency, traffic, errors and saturation are the four to start from.
9. **Every alert must be actionable.** One that fires and is routinely ignored should be deleted:
   it is training you to ignore the others.
10. **Store a timestamp, not a boolean.** A nullable `ProcessedAtUtc` lets you alert on *how far
    behind* the outbox is; an `IsProcessed` flag can only say whether.

## [Module 12 — Testing](module-12-testing/)

> The card.

1. **Many fast tests, few slow ones.** Invert the pyramid and the suite takes forty minutes, so
   nobody runs it, so it catches nothing.
2. **A domain test needs no mocks, no database and no `async`.** If yours does, the design is
   telling you something.
3. **Mock what you do not control; use the real thing for what you own.** Never mock your own
   domain objects.
4. **Name tests `Method_Scenario_ExpectedOutcome`.** A failure should be diagnosable from the name
   alone, without opening the file.
5. **Assert the absence of side effects too.** "It failed" is half the assertion; "…and staged
   nothing for insertion" is the other half.
6. **`UseInMemoryDatabase` is not a database.** No schema, no unique indexes, no CHECK
   constraints, no transactions, no `rowversion`, no raw SQL — so it passes tests that production
   fails. Use a real SQL Server and give each run its own throwaway database.
7. **Assert against the database, not the API.** Asking the API whether the API worked is
   circular.
8. **Declare wire contracts separately in the test project.** A test that shares the production
   DTO cannot detect a breaking change to the contract.
9. **Test outcomes, not interactions.** "Submitting reserves stock" survives a refactor; "it
   called `Reserve` once with those arguments" breaks on the next one.
10. **Turn conventions into build failures.** Architecture tests cost milliseconds and keep the
    design honest through years of team turnover — but make the failure message name the
    offender, or people will delete the test.
11. **Do not chase coverage.** 100% with weak assertions is worse than 70% with strong ones,
    because it feels safe.

## [Module 13 — Configuration, security and deployment](module-13-deployment/)

> The card. Print it and work down it before a first production deploy.

1. **No secret in the repository, ever — and if one gets in, rotate it.** Deleting it from the
   working tree leaves it in the history, and history gets cloned.
2. **The JSON file documents the shape; the environment supplies the value.** Environment
   variables win over JSON, with `__` standing in for `:`.
3. **`ValidateOnStart` on everything the app cannot run without.** A misconfigured deploy should
   fail to start, loudly, not serve 500s an hour later.
4. **Let a known CVE fail the build.** NuGetAudit plus warnings-as-errors plus central package
   management makes the fix a one-line diff.
5. **Newest is not the goal. Patched and compatible is.** Read the advisory, and check what the
   framework package was built against before jumping a major version.
6. **Lock the restore in CI.** A build that can silently pick up a version you never tested is not
   reproducible.
7. **Migrations are a pipeline step, not a startup step.** Instances race, and the application
   should not hold schema-altering permissions at runtime.
8. **Generate the script with `--idempotent`, and have a human read it.** EF sometimes decides a
   rename is a drop-and-recreate.
9. **Expand, migrate, contract.** Three deploys is the actual price of zero downtime — know that
   before you promise it.
10. **`aspnet` runtime image, not `sdk`; restore before copying source; run as non-root.** 100 MB
    instead of 800, a cache that actually works, and no compiler on a production host.
11. **Liveness dependency-free, readiness not.** Same rule as module 11, this time in the
    orchestrator's YAML.
12. **An untested backup is a hope, not a backup.** Restore one before you need to.

## [Module 14 — Performance](module-14-performance/)

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

## [Module 15 — ASP.NET Core in depth](module-15-aspnetcore-in-depth/)

> The card. Half of these are the answer to "why does it return 401 with a valid token?"

1. **The pipeline is one nested function, composed in reverse at build time.** Down on the way in,
   up on the way out, `await next()` is the hinge, and not calling it short-circuits.
2. **Order is behaviour.** Exception handling first, rate limiting before authentication,
   authentication before authorization, forwarded headers before anything that reads the scheme or
   the client IP.
3. **Routing matches; endpoints execute; authorization lives in the gap.** Before `UseRouting`,
   `GetEndpoint()` is null and there is nothing to authorize against.
4. **A route constraint is matching, not validation.** `{id:guid}` given "abc" produces a 404,
   because no route matched — not a 400.
5. **Convention-based middleware is constructed once.** Scoped services go in `InvokeAsync`'s
   parameters, never the constructor.
6. **Would this still need to happen if the command came from a queue?** Yes ⇒ pipeline
   behaviour. No ⇒ middleware or endpoint filter.
7. **A non-nullable parameter is a required parameter.** The binder never sees your property
   initialiser.
8. **`TypedResults` over `Results`.** The type carries the status and the payload, so a unit test
   can assert on it and OpenAPI can infer the shape.
9. **`IOptions` for startup values, `IOptionsSnapshot` per request, `IOptionsMonitor` in
   singletons.** A `BackgroundService` taking `IOptionsSnapshot` is a captive dependency.
10. **`ValidateOnStart()`, always.** Fail at deploy, not at the first login.
11. **401 is authentication, 403 is authorization.** A valid-looking token returning 401 means
    authentication failed silently: check issuer, audience, signing key and clock skew, in that
    order.
12. **Policies at the call site, roles inside the policy.** Roles are data; policies are decisions,
    and a decision should live in one place.
13. **Behind a proxy, `UseForwardedHeaders` first** — or every client IP is the proxy's, and your
    per-user rate limiter partitions all anonymous traffic into one bucket.

## [Module 16 — The layer map: C#, LINQ, SQL and ASP.NET Core](module-16-the-layer-map/)

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

## [Module 17 — Landing a .NET job in Modena and Bologna](module-17-career-emilia-romagna/)

> The card. Technique is the other twenty-eight modules; this is the part candidates get wrong.

1. **Search the whole Modena–Bologna corridor**, not one city. It is one labour market and it is
   forty minutes wide.
2. **Lead with SQL.** Almost nobody at junior or mid level can explain why a query is slow or read
   an execution plan. Module 07 puts you in a small minority — say so early.
3. **Apply at about 60% match.** Italian job adverts list a wish, not a filter.
4. **Applications start in week 5, not week 12.** Pipelines take weeks; the technical work and the
   search run in parallel or you finish the course unemployed.
5. **Put your Italian on the CV as a trajectory.** "Italiano: A2, in studio attivo" beats silence,
   and answers the question every recruiter is silently asking.
6. **State your work authorisation in one line.** Recruiters discard ambiguity rather than
   investigate it.
7. **Two pages, PDF, the GDPR line, no Europass, project before education.** For a career changer,
   a public repository with a real architecture beats almost everything else on the page.
8. **Re-skin LogiFlow to the local domain before you send it** — production orders, work centres,
   lot traceability. Mostly renaming, and it changes what you are in the room.
9. **Be able to defend every decision in it, including the compromises the README admits to.**
   Naming the condition under which you would *not* do something is what reads as senior.
10. **Prefer employment to `partita IVA` for a first job here.** Fixed hours at one client's office
    with their equipment is not freelancing; it is the burden without the protection.
11. **Ask about trasferta, the legacy estate, who owns the database, and which CCNL.** Their
    answers tell you whether you want the job; the questions tell them you have worked on real
    systems.
12. **Compare offers on RAL, contract type, CCNL, meal vouchers and travel — not on the number
    alone.** And verify every figure in this module before you negotiate: they move, and I cannot
    check them for you.

## [Module 18 — Blazor: a UI over the API](module-18-blazor/)

> The card.

1. **Choose the render mode on users, network and secrets** — not on preference. Server: fast
   first paint, no download, the token stays server-side, one stateful circuit per user.
   WebAssembly: heavy first load, no server state, scales like static files.
2. **In Blazor Server, component state is server memory.** Paging a large list is a capacity
   decision, not a nicety.
3. **`ErrorBoundary` is not optional, and it latches.** Until you call `Recover()`, the error
   message never goes away.
4. **Do not throw for expected failures.** A 404 or a 409 is an ordinary outcome; here an
   exception costs the user their session.
5. **A typed client, and a `DelegatingHandler` for the token.** One place owns the base address,
   the auth header, the JSON options and the error translation — including for the code someone
   adds next year.
6. **Duplicate the contract across a published boundary.** A rename in the Application layer
   *should* break the client; if the UI simply is the server's types, there is no contract, only
   coupling. And write enum values out explicitly when they travel as integers.
7. **Let the server own the state machine.** Render the buttons from `AllowedTransitions`; the
   moment the UI reimplements the transition table, the two start drifting.
8. **`OnInitializedAsync` runs twice on first load and never again on a route change.** Prerender
   plus interactive is two fetches; navigation between two URLs on the same `@page` reuses the
   instance. Load data in `OnParametersSetAsync`.
9. **`@key` every repeated element.** Without it the diff matches by position, and state inside a
   row — input text, focus, a checked box — follows the position instead of the record.
10. **Debounce with an awaited `Task.Delay` and a token, not a `Timer`.** The continuation resumes
    on the renderer's synchronisation context; a timer callback does not, and that is where the
    race lives. Anything touching state from off that context needs
    `InvokeAsync(StateHasChanged)`.
11. **Two cancellation sources: one for the keystroke, one for the request in flight.** Share them
    and the next keystroke cancels the request it was waiting for.
12. **Fire-and-forget needs the trio: a discard, a token, and a `Dispose` that cancels it.**
    Otherwise navigating away leaves a timer holding a disposed component.
13. **Format anything that becomes CSS with `InvariantCulture`.** On an Italian machine
    `width:33,33%` is silently dropped by the browser and nothing anywhere mentions culture.
14. **Reload after a 409 and keep the error visible.** A UI showing only the error leaves the user
    staring at a screen that disagrees with the database.
15. **Respect `prefers-reduced-motion` by shortening durations, not removing animations** — an
    element animated with `both` that never runs is an element that never appears.

## [Module 19 — Memory, the heap, and the garbage collector](module-19-memory-and-gc/)

1. **Allocation is a pointer bump; collection is what costs — and it costs in proportion to
   SURVIVORS, not to garbage.** A million short-lived objects are cheap. Ten thousand long-lived
   ones are not.
2. **A leak in .NET is a reference you forgot, not memory you forgot to free.** The first
   suspects are an unsubscribed event, a static collection, and a cache with no eviction.
3. **A value type lives wherever its container lives.** "Structs are on the stack" is wrong: a
   struct field of a class is on the heap, and a captured local is on the heap too.
4. **85,000 bytes is the Large Object Heap line.** Above it, an object is born in gen 2 and is
   not compacted. Pool the buffer instead of crossing it.
5. **Gen 0 is cheap because of the write barrier and the card table** — old-to-young references
   are recorded when written, so a nursery collection never has to scan the old heap.
6. **A finalizer is a safety net for unmanaged memory, and nothing else.** It costs an extra
   generation of lifetime, runs on someone else's thread at an unknown time, and may never run.
7. **If you own something disposable, you are disposable — and if you did not create it, do not
   dispose it.** Both halves cause outages.
8. **`Dispose` must be idempotent and must not throw.** It runs on the exception path, where
   throwing hides the real failure.
9. **`await using` whenever the type offers `IAsyncDisposable`.** A plain `using` on a type that
   implements both silently picks the blocking one.
10. **A rented buffer is dirty and is bigger than you asked for.** Never trust `.Length`, always
    `Return` in a `finally`, and clear it if it held anything private.
11. **Never call `GC.Collect()` in production.** It forces a full blocking collection and
    promotes every survivor — the exact opposite of what you wanted.
12. **Server GC for servers, and give the container a memory limit the runtime can see.** One
    heap per core is throughput; one heap per core inside an unaware 512 MB container is an OOM
    kill.

## [Module 20 — Equality, hashing, and choosing a collection](module-20-equality-and-collections/)

1. **`==` is static, `Equals` is virtual.** The compiler picks `==` from the declared type; the
   runtime picks `Equals` from the actual one. In generic code with an unconstrained `T`, `==` is
   reference equality — use `EqualityComparer<T>.Default`.
2. **Override `Equals` and you must override `GetHashCode`.** Equal objects must hash equally, or
   every hash-based collection quietly loses your data.
3. **The hash of a key must never change while it is in the table.** Mutate it and the entry
   becomes unreachable and unremovable — so dictionary keys are immutable, full stop.
4. **`HashCode.Combine`, never XOR and never sum.** XOR is commutative, so `(1,2)` and `(2,1)`
   collide, and in a composite key that is half your rows in one bucket.
5. **A `GetHashCode()` value is valid for one process, for one run.** The string seed is
   randomised per process. Never persist it, send it, or shard on it.
6. **A struct used as a dictionary key must implement `IEquatable<T>`** — otherwise every lookup
   boxes and falls back to a reflection-driven comparison. `readonly record struct` gives you
   both for free.
7. **If `CompareTo` returns 0, `Equals` must return true.** Sorted collections use only the
   first, hashed collections only the second; letting them disagree gives you two truths.
8. **Never write `a.Value - b.Value` in a comparer.** It overflows and reverses the sign.
   `CompareTo`, always.
9. **`Contains` in a loop over a `List<T>` is O(n²).** Build a `HashSet<T>` first. This is the
   most common real performance bug in ordinary business code.
10. **Size a dictionary you are about to fill.** Growth rehashes every entry, and the constructor
    takes the capacity.
11. **`TryGetValue` and `TryAdd` are one lookup; `ContainsKey` plus an indexer is two.**
12. **Dictionary enumeration order is undefined.** It looks like insertion order until the first
    removal, and then it does not.
13. **`ConcurrentDictionary` makes each operation atomic, not each sequence** — and `GetOrAdd`
    may invoke your factory more than once, so the factory must be cheap and side-effect free.

## [Module 21 — Threading, locks, and the memory model](module-21-threading-and-memory-model/)

1. **A task is not a thread, and a thread is not a core.** Tasks are cheap promises; threads cost
   about a megabyte and a millisecond; cores are the only real parallelism you have.
2. **The pool grows by roughly one thread per second above the core count.** That single number
   is the entire mechanism of thread-pool starvation.
3. **p99 latency climbing while CPU sits idle means threads, not compute.** The cause is always
   blocking: `.Result`, `.Wait()`, a synchronous I/O call, or lock contention.
4. **`counter++` is read-add-write, not one operation.** Two threads lose updates immediately, and
   at volume they lose most of them.
5. **`Interlocked` for one variable, `lock` for an invariant across several, a channel for not
   sharing at all.** In that order of preference.
6. **`Interlocked.CompareExchange` is optimistic concurrency at the CPU level** — the same
   read/compute/conditional-write/retry shape as a `rowversion` check.
7. **Lock on a private readonly object, never on `this`, a `Type`, or a string.** Anything a
   stranger can lock on is a deadlock written in someone else's file.
8. **Never `await` inside a lock.** A monitor belongs to a thread; a continuation may resume on
   another. `SemaphoreSlim.WaitAsync` is the async critical section.
9. **Take multiple locks in one globally consistent order**, and never call unknown code while
   holding one.
10. **Without a fence, another thread may never see your write.** The compiler may hoist it, the
    CPU may reorder it. `lock`, `Interlocked` and `volatile` are the fences.
11. **`volatile` does not make `++` atomic.** It orders individual reads and writes; the
    read-modify-write in between is still three steps.
12. **`long` and `double` are not atomic on 32-bit, and no multi-word struct ever is.** A torn
    read returns a value that never existed.
13. **`Parallel`/PLINQ are for CPU-bound work only**, the body must be thread-safe, and below a
    few thousand items the partitioning costs more than it saves.
14. **`Task.WhenAll` throws only the first exception.** The rest are on the task's `Exception`
    property, silent unless you look.
15. **ASP.NET Core has no synchronization context, so sync-over-async passes every test and
    deadlocks in Blazor Server and WPF.**

## [Module 22 — Inside the CLR: compilation, types, and the JIT](module-22-clr-internals/)

1. **A .dll is IL plus metadata, not machine code.** That is why reflection, decompilers and
   source generators are possible, and why shipping a build is not obfuscation.
2. **The JIT compiles per method, on first call.** First-request latency and misleading console
   timings both come from this.
3. **Tier 0 gets you started, tier 1 gets you fast, OSR bridges a long-running loop.** You do not
   configure it; you just stop being surprised by it.
4. **ReadyToRun for faster startup with no loss of peak speed; Native AOT when you can give up
   runtime code generation.** No `Expression.Compile`, no `Reflection.Emit`, and reflection over
   trimmed types throws.
5. **Every object carries a sync-block index and a method-table pointer.** That is `GetType()`,
   virtual dispatch, `lock`, and boxing, all from one 16-byte header.
6. **Generics are instantiated at run time: shared code for reference types, specialised code per
   value type.** Type safety and no boxing, at the cost of JIT time per struct instantiation.
7. **A static field in a generic type is per closed type.** `Cache<int>` and `Cache<string>` are
   two caches — a feature, and a foot-gun if you did not intend it.
8. **A generic constraint removes the boxing an interface parameter forces.** Same method body,
   240,000 bytes versus zero.
9. **Reflection is slow because of the metadata lookup and the boxing, not because it is
   reflection.** Cache the `MemberInfo`, build a delegate, and the cost is gone.
10. **Prefer a source generator to run-time reflection.** Compile-time work is faster, debuggable,
    trimming-safe, and turns run-time failures into build errors.
11. **Pinning fragments the heap.** `fixed` stops the GC moving an object; `Span<T>` is the safe
    answer for nearly every case that used to need it.

## [Module 23 — Text, culture, time, and serialization](module-23-text-culture-serialization/)

1. **`string` is immutable, so every concatenation allocates.** Fine for three; O(n²) in a loop.
   `StringBuilder`, or `string.Create`.
2. **A `char` is a UTF-16 code unit, not a character.** An emoji has `Length == 2`. Never reverse
   or truncate by `char` index, and normalise before comparing text from different sources.
3. **Ordinal for identifiers, Invariant for persistence, Current for humans.** Three categories,
   no fourth, and every call site belongs to exactly one.
4. **An unqualified `ToString()`/`Parse` is a latent bug on a non-English machine.** On `it-IT`,
   `"1234.5"` parses to `12345`, silently.
5. **`ToLower()` without a culture fails in Turkish.** The dotless `ı` breaks the oldest string
   comparison in the book. Use `OrdinalIgnoreCase` rather than case-folding at all.
6. **Anything that becomes CSS, JSON, a URL or SQL is formatted with `InvariantCulture`.**
   `width:33,33%` is silently dropped by every browser.
7. **Ordinal is both the correct answer and the fast one** for identifiers — it is a memory
   compare, not a collation walk.
8. **Store UTC, transmit ISO-8601 with an offset, convert only at the edge.**
9. **`DateTime.Kind` is not persisted by most databases**, so a Utc value comes back Unspecified
   and shifts on the next conversion. Use `DateTimeOffset` for an instant.
10. **An offset is not a time zone.** Only `TimeZoneInfo` knows that 02:30 does not exist one night
    in March and happens twice one night in October.
11. **Inject `TimeProvider`.** `DateTime.UtcNow` is a hidden static dependency, and it is why
    month-end and expiry logic cannot be tested.
12. **`JsonSerializer` serializes the DECLARED type.** A derived object assigned to a base-typed
    variable silently loses its extra properties. `[JsonDerivedType]`, or serialize
    `n.GetType()`.
13. **Register one `JsonSerializerOptions` and reuse it.** It caches per-type metadata; a new
    instance per call throws that away, and mismatched instances give you two casing conventions.
14. **Serialize enums as strings across a published boundary** — and pin the numeric values
    anyway.
15. **Deserializing to `object` yields a `JsonElement` over a buffer** that throws once the
    document is disposed.
16. **Use JSON source generation.** Compile-time, no reflection, trimming- and AOT-safe.

## [Module 24 — Security for a .NET API](module-24-security/)

1. **`UseAuthentication` before `UseAuthorization`.** Reversed, `User` is empty when the policy
   runs — every protected endpoint 401s and it looks like a token bug.
2. **A JWT is signed, not encrypted.** Anyone holding it can read every claim, so nothing private
   goes in a token.
3. **Validate issuer, audience, lifetime, signature and algorithm.** Never trust the `alg` the
   token names.
4. **A JWT cannot be revoked, so keep it short-lived** and put the revocable state in a refresh
   token you store server-side.
5. **Use asymmetric signing beyond a single service.** With HMAC, everything that can verify can
   also mint.
6. **Named policies at the endpoint, never inline role lists.** The rule is then defined once and
   can change shape without touching the endpoints.
7. **Hash passwords with a slow, salted KDF** — Argon2id or PBKDF2 with a high iteration count.
   A fast hash is the attacker's dream, so SHA-256 alone is a mistake.
8. **Compare secrets in constant time**, and return the same message and timing for "no such user"
   as for "wrong password".
9. **Parameterise every query.** The value is transmitted separately from the SQL text, so its
   content can never change the statement's structure. Escaping is not the same thing.
10. **A column or table name cannot be a parameter — use an allow list.** Dynamic `ORDER BY` is
    where injection survives in an EF codebase.
11. **Bind to a request DTO, never to an entity.** Over-posting becomes structurally impossible
    rather than merely unlikely.
12. **Object-level authorization belongs in the handler, next to the data.** An endpoint policy
    cannot express "may this user see *this* row", and IDOR is the vulnerability scanners miss.
13. **Every list endpoint has a maximum page size.** An unbounded one is a denial-of-service
    parameter with a friendly name.
14. **Return a `ProblemDetails` with a correlation id; log the detail server-side.** A stack trace
    in a response is reconnaissance.
15. **CORS is a browser policy, not a security boundary.** It does nothing against a non-browser
    client.
16. **Anti-forgery is for cookie auth, not bearer tokens** — because the browser attaches cookies
    for you and does not attach an `Authorization` header.
17. **A secret committed to git is compromised; rotate it.** Deleting the commit is not a remedy.
18. **Scan dependencies in CI.** A transitive package runs with your privileges.

## [Module 25 — Distributed systems and integration](module-25-distributed-systems/)

1. **A timeout tells you nothing about whether the work happened.** The reply can be lost after
   the commit — which is why every retryable operation must be idempotent.
2. **You cannot atomically write to two systems.** Save-then-publish loses messages;
   publish-then-save invents them. Make it one write: the outbox.
3. **Exactly-once delivery does not exist.** Choose at-least-once and make the consumer
   idempotent; that is what "exactly-once processing" really means.
4. **Idempotency is designed in, not added.** Unique constraints, a processed-message table,
   conditional updates, and setting values rather than incrementing them.
5. **`SET x = 5` is idempotent; `SET x = x - 1` is not.** When you must decrement, do it with a
   version check.
6. **Never retry a non-idempotent operation, and never retry a 4xx.** The answer will not change,
   and you have multiplied one bad request.
7. **Exponential backoff with jitter.** Fixed intervals synchronise thousands of clients into a
   thundering herd that keeps the recovering service down.
8. **Cap total elapsed time, not just the retry count.** Otherwise the caller times out and
   retries on top of you.
9. **A circuit breaker protects the caller as much as the callee.** Without one, their outage
   consumes all your threads and becomes your outage.
10. **Every remote call has a timeout, and the cancellation token is threaded all the way down.**
11. **Eventual consistency is a design decision, not a defect** — but "how stale may this be?" is
    a question you must answer explicitly, per read.
12. **A saga compensates; it does not roll back.** A refund is a new business fact, visible to the
    customer, not an undo.
13. **Order is guaranteed only within a partition.** Partition by aggregate id, or make handlers
    order-insensitive with a version check.
14. **Propagate the trace context.** One request across five services must be one trace, or you
    are debugging by timestamp.
15. **Liveness and readiness are different questions.** Conflating them turns a dependency's
    outage into a restart loop.
16. **Microservices buy independent deployment for independent teams.** The benefit is
    organisational; every cost is technical. At two teams you have paid for all of it and bought
    nothing — the default is a modular monolith.
17. **Cut services by business capability, never by layer or table.** The seam is where the
    vocabulary changes; services that must be released together are a distributed monolith.

## [Module 26 — Design patterns and SOLID, in this codebase](module-26-patterns-and-solid/)

1. **Single responsibility is about REASONS TO CHANGE, not size.** Two stakeholders who can each
   demand a change to one class means two responsibilities.
2. **Dependency inversion means the high-level module owns the abstraction.** `IOrderRepository`
   lives in Application, not next to its implementation — that placement is what makes the
   dependency arrow point inward.
3. **A subtype must be substitutable without the caller knowing.** A derived member that throws
   `NotSupportedException` is a Liskov violation, and `sealed` is a good default because of it.
4. **Never expose `IQueryable` from a repository.** The moment you do, the abstraction is
   decorative and EF Core has leaked into the layer that was supposed to be free of it.
5. **A pattern is an answer to a problem you can state.** If you cannot say what would go wrong
   without it, you do not need it yet.
6. **Duplicate twice; abstract on the third.** A wrong abstraction costs far more than duplication,
   because everything else gets built on top of it.
7. **Prefer a static factory method to a factory class**, and let it return a `Result` so an
   invalid object cannot be constructed at all.
8. **Injecting `IServiceProvider` hides your dependencies.** Constructor parameters are a public
   declaration of what a class needs; resolving from the container is a secret.
9. **Map explicitly across a published boundary.** A renamed property should be a compile error,
   not a null at run time.
10. **Primitive obsession is a type-system problem with a simple fix.** Two adjacent `string`
    parameters can be swapped silently; `Sku` and `CustomerName` cannot.
11. **The GoF singleton is a global variable.** Use the container's singleton lifetime, which is
    substitutable in a test.
12. **An anemic domain model is the default, not a design.** Behaviour belongs next to the data it
    protects.

## [Module 27 — C# version by version](module-27-csharp-versions/)

1. **The C# version is chosen by the target framework, not the SDK.** `net10.0` means C# 14;
   `global.json` pins the SDK for build reproducibility, and the TFM picks the language.
2. **The four that changed the language are generics, LINQ, async/await and nullable reference
   types.** Everything else is refinement.
3. **Generics were added for boxing and type safety, not syntax** — and unlike Java's they are a
   runtime feature, which is why `typeof(List<int>)` exists.
4. **LINQ needed four features to exist**: extension methods, lambdas, anonymous types and
   expression trees. The last one is the entire C#-to-SQL border.
5. **`foreach` was fixed in C# 5; `for` was not.** A `for` loop still captures one shared variable
   — and it is still the loop you write when building a pipeline.
6. **Exception filters (`when`) run before the stack unwinds.** That is why they preserve a better
   crash dump than catch-log-rethrow.
7. **`readonly struct` and `ref struct` are performance features with semantics**: one stops
   defensive copies, the other guarantees stack-only, which is what makes `Span<T>` safe.
8. **Nullable reference types are compile-time only** — the single most valuable feature for a
   business codebase, and it still does not check anything at run time.
9. **`static abstract` interface members enable generic math, and cannot appear in an expression
   tree.** That collision is where EF Core meets modern C#.
10. **`System.Threading.Lock` (C# 13) makes "do not lock on a shared object" a type-level rule**
    rather than a convention nobody enforces.
11. **The `field` keyword (C# 14) removes the backing field**, which is the most common
    boilerplate left in the language.
12. **Know what the version you are interviewing for supports.** Being able to say "that needs C#
    11, so on .NET 6 you would write it this way" is worth more than knowing the newest feature.

## [Module 28 — Industrial software and the IT/OT boundary](module-28-industrial-and-ot/)

1. **You are hired for Level 3, and the job is the two boundaries.** Below is a machine with no
   schema and no patience; above is an ERP that thinks in money. The middle is ordinary C#.
2. **The machine layer has no schema.** A register is a number at an address, and units, scaling,
   word order and what counts as `running` all live in a spreadsheet outside the protocol.
3. **A value is a sample, not the truth.** It was taken at a time that is not now, and asking
   again gets you a different one rather than the same one confirmed.
4. **Never poll for events.** Subscribe. The interesting things on a line are shorter than any
   interval you can afford, and the ones that matter most are the shortest.
5. **Two timestamps, always** — when the machine says it was true, and when you received it. One
   column throws away the only latency measurement the protocol gives you for free.
6. **Store quality, never just the value.** `Bad` and `Uncertain` are readings, not nulls, and
   averaging them with good ones invents data that nobody measured.
7. **The machine does not wait for your consumer.** Backpressure is a choice between losing the
   past and losing the present, and it must be made deliberately rather than defaulted into.
8. **Store the events and compute the number.** A stored OEE cannot be explained, cannot be
   recomputed when the definition changes, and the definition will change.
9. **Whoever defines planned downtime sets the OEE.** The same shift is 84% or 76% depending on
   one decision that involves no machine at all.
10. **Performance above 100% is a data-quality alarm, not a good day.** The configured ideal cycle
    time is wrong, or somebody ran the line over its rated speed.
11. **Traceability is a legal obligation, not a feature.** In food, pharma and automotive the
    question is which lot, which machine, which shift — and the answer must survive years.
12. **Deadlock on a floor is prevented, not detected.** Acquire zones in a total order or grant a
    whole route atomically, because nothing times out when two vehicles are nose to nose.
13. **Zone size is the throughput dial**, and tuning it beats clever code — coarse zones serialise
    moves that never actually conflict.
14. **The line is on an isolated network on purpose.** Data leaves OT through a gateway, outward
    only, and "we'll just put it in the cloud" is a proposal, not a plan.
15. **Assume you may not patch the HMI.** A Windows box from 2009 whose vendor warranty forbids
    you to touch it is normal; you compensate around it rather than fixing it.
16. **Commissioning is the job, not the end of it.** Half of this work happens on site, with the
    line stopped and people waiting, and the candidate who knows that is the one who lasts.
