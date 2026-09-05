# The Laws of C#

> **[`GOLDEN-RULES.md`](GOLDEN-RULES.md) is the course, module by module.** This page is the same
> knowledge reorganised by *concept* — the invariants of the language and its runtime, in twelve
> books, so that when something surprises you it is findable by what it is about rather than by
> which module happened to teach it.
>
> These are not style preferences. Each one is a fact about how C# or the CLR behaves, and each is
> stated so you can say **why** in the next breath. A law you can recite but not justify is worth
> nothing in an interview and less in production.

**How to use it.** Read a book at a time, and stop at every law you cannot justify in two
sentences. That is your revision list. Most laws end with a way to *observe* them — a
`dotnet run` command, or a file in this repository. Observation is the difference between knowing
a sentence and knowing a thing.

```bash
cd labs/Labs.Playground && dotnet run list    # 29 runnable demos
```

---

## The ten that are really one law

Nearly everything below is a consequence of ten ideas. If you internalise these, most of the rest
is derivable rather than memorised.

1. **A value type is copied; a reference type is shared.** — Book I
2. **Equality is a contract you opt into, and hashing is half of it.** — Book II
3. **Nullability is a compile-time claim, not a runtime guarantee.** — Book III
4. **The GC charges you for survivors, not for garbage.** — Book IV
5. **A query is a recipe; nothing runs until someone enumerates it.** — Book V
6. **`async` frees a thread; it does not create one.** — Book VI
7. **`IEnumerable` runs here, `IQueryable` runs there — and the line between them is where the
   bugs live.** — Book VII
8. **Expected outcomes are values; bugs and infrastructure failures are exceptions.** — Book VIII
9. **Every conversion between text and data makes a culture decision, and the default is usually
   wrong.** — Book IX
10. **A boundary is a contract: validate crossing in, project crossing out.** — Book X

---

## Book I — Types and values

**1. A value type is copied on every assignment, argument pass and return; a reference type
copies only the reference.** Everything about structs versus classes follows from this one
sentence. → `dotnet run structs`

**2. A value type lives wherever its container lives.** "Structs are on the stack" is wrong in
exactly the cases that matter: a struct field of a class is on the heap, and a captured local is on
the heap inside a closure object.

**3. A struct larger than about 16 bytes costs more than an allocation**, because the copy happens
on every assignment, argument pass and return — repeatedly, rather than once.

**4. `readonly` on a struct is not decoration.** Without it, the compiler makes a defensive copy on
every member access through a `readonly` field or an `in` parameter.

**5. A mutable struct behaves differently depending on the collection holding it.** `list[0]`
returns a copy; `array[0]` gives a reference to the slot. Same syntax, opposite behaviour — which is
why mutable structs are discouraged and `readonly` makes the bug uncompilable. → `dotnet run structs`

**6. Boxing is an allocation that does not appear in the source.** Any time a value type becomes
`object`, an interface, or an unconstrained generic argument, it is copied to the heap.
→ `dotnet run boxing`

**7. A generic constraint removes boxing that an interface parameter forces.** `M<T>(T x) where T :
IShout` compiles to a direct call; `M(IShout x)` boxes first. → `dotnet run generics`

**8. An `enum` is a named integer, and `(Currency)999` is a legal value that matches no case.**
Reach for a smart enum the moment a member carries data or behaviour.

**9. Pin the numeric values of any enum you persist or serialize.** Insert a member alphabetically
without them and every existing row and stored message silently changes meaning.

**10. A `switch` expression over an enum with no `default` arm is a tripwire.** Adding a member
without handling it fails the build. Adding `default =>` throws that safety net away.

**11. Array covariance is checked at run time; generic variance is checked by the compiler.**
`object[] o = new string[1]; o[0] = 42;` compiles and throws. `out` means it only comes out, `in`
means it only goes in, and anything that does both is invariant. → `dotnet run variance`

**12. A static field in a generic type is per *closed* type.** `Cache<int>` and `Cache<string>` are
two different caches. A feature when you meant it, a bug when you did not.

**13. `double` cannot represent `0.1`, and money is therefore `decimal`.** Ten additions of `0.1`
are already wrong. → `dotnet run numbers`

**14. Integer overflow is silent by default.** `<CheckForOverflowUnderflow>` makes the whole
assembly `checked`, and for business code the cost is nothing.

**15. `Math.Round` is banker's rounding by default** — to even, not away from zero. It will pass
every test you thought to write and lose a cent per invoice.

---

## Book II — Equality and identity

**16. `==` is static; `Equals` is virtual.** The compiler picks `==` from the declared type; the
runtime picks `Equals` from the actual type. Cast a record to `object` and `==` becomes reference
equality. → `dotnet run equality`

**17. In generic code with an unconstrained `T`, `a == b` is reference equality** — even when `T`
is a record. Use `EqualityComparer<T>.Default`.

**18. Override `Equals` and you must override `GetHashCode`.** Equal objects that hash differently
are lost by every hash-based collection, silently.

**19. Equal objects must hash equally; unequal objects may collide.** The first makes lookup
correct; the second makes hashing possible at all.

**20. The hash of a key must not change while it is in a hash table.** Mutate it and the entry
becomes unreachable *and* unremovable — so dictionary keys are immutable. → `dotnet run hashcode`

**21. `HashCode.Combine`, never XOR and never sum.** XOR is commutative, so `(1,2)` and `(2,1)`
collide — in a composite key, that is half your data in one bucket.

**22. A `GetHashCode()` value is valid for one process, for one run.** The string hash seed is
randomised per process. Never persist it, transmit it, or shard on it.

**23. A record's generated `Equals` begins with an `EqualityContract` type check**, so a derived
record is never equal to its base even with identical fields.

**24. A struct used as a dictionary key must implement `IEquatable<T>`**, or every lookup boxes
and falls back to a field-by-field comparison. `readonly record struct` gives you both.

**25. If `CompareTo` returns 0, `Equals` must return true.** Sorted collections use only the first
and hashed collections only the second; letting them disagree gives you two contradictory truths.

**26. Never write `a.Value - b.Value` in a comparer.** It overflows and reverses the sign.

**27. Record for values, class for identity, struct for small immutable data.** `readonly record
struct` is all three properties at once, and it is what a strongly-typed id should be.

---

## Book III — Null

**28. Nullable reference types are compile-time only.** Nothing is checked at run time, so data
from JSON, a database or an unannotated library can still be null despite its type.

**29. Therefore you still validate at every boundary.** The compiler's guarantee ends where your
process does.

**30. `= null!` is a promise you are making to the compiler.** It is legitimate only where
something outside the type system guarantees assignment — EF Core materialisation, and little else.

**31. `[NotNullWhen(true)]` teaches the compiler about your own methods**, which is how correct
calling code stops needing `!`.

**32. `?.` short-circuits the whole chain, and returns null rather than throwing.** Which is
sometimes the bug: a silent null is harder to find than an exception at the point of failure.

**33. `??=` assigns only when null**, and is not thread-safe. Two threads can both see null.

**34. Nullable *value* types are a different mechanism entirely.** `int?` is `Nullable<int>`, a real
struct with real runtime behaviour; `string?` is an annotation the runtime never sees.

**35. A null in a database is not a null in C#.** SQL's three-valued logic means `NULL = NULL` is
unknown, not true — so `WHERE x = @p` never matches a null row. `IS NULL`, always.

---

## Book IV — Memory and lifetime

**36. Allocation is a pointer bump; collection is what costs.** And it costs in proportion to
**survivors**, not to garbage. → `dotnet run gc`

**37. A million short-lived objects are cheap; ten thousand long-lived ones are not.** This
reverses most naive advice about allocating in loops.

**38. A leak in .NET is a reference you forgot, not memory you forgot to free.** The suspects, in
order: an unsubscribed event, a static collection, a cache with no eviction, a captured closure
held by something long-lived. → `dotnet run leaks`

**39. A `static` field is a GC root.** That is the definition, not a metaphor.

**40. A lambda that touches one field captures `this` — the whole object.** Hand it to a timer or a
long-running task and the entire graph is pinned.

**41. 85,000 bytes is the Large Object Heap line.** Above it, an object is born in gen 2 and is not
compacted, so the LOH fragments: plenty of free memory, and the allocation still fails.

**42. Gen 0 stays cheap because of the write barrier and the card table.** Old-to-young references
are recorded when written, so a nursery collection never scans the old heap.

**43. A finalizer is a safety net for unmanaged memory and nothing else.** It costs the object an
extra generation of lifetime, runs on another thread at an unknown time, and may never run.
→ `dotnet run finalizers`

**44. If you own something disposable, you are disposable — and if you did not create it, do not
dispose it.** Both halves cause outages.

**45. `Dispose` must be idempotent and must not throw.** It runs on the exception path, where
throwing replaces the real error with yours.

**46. `await using` whenever the type offers `IAsyncDisposable`.** A plain `using` on a type
implementing both silently picks the blocking one. → `dotnet run disposal`

**47. Nested `using`s dispose in reverse order** — which is what you want, because the inner thing
was built on the outer one.

**48. A rented buffer is dirty, and bigger than you asked for.** Never use `.Length`, always
`Return` in a `finally`, and clear it if it held anything private. → `dotnet run pooling`

**49. Never call `GC.Collect()` in production.** It forces a full blocking collection and promotes
every survivor — the opposite of what you wanted.

**50. A `Span<T>` cannot be a class field, cannot be captured by a lambda, and cannot cross an
`await`.** It is a stack-only type by design, and that restriction is what makes it safe.

---

## Book V — Laziness and evaluation

**51. A LINQ query is a recipe, not a result.** Nothing runs until something enumerates it.
→ `dotnet run deferred`

**52. Enumerate twice and it runs twice.** Against a database that is a second round trip.
Materialise once with `ToList()` when you will use it more than once.

**53. A deferred query sees changes made after it was defined**, because it re-reads its source
each time.

**54. Compose while it is a query; materialise once, at the end.** Every `ToList()` in the middle
of a chain is a decision to do the rest of the work in memory.

**55. `yield return` turns the method into a state machine class**, so calling it executes none of
your code — it only constructs the object.

**56. Therefore argument validation in an iterator runs at the wrong time**, on the first
`MoveNext`, in a stack frame that has nothing to do with the caller. Validate eagerly in a wrapper
and delegate to a private iterator. → `dotnet run iterators`

**57. `finally` in an iterator runs on `Dispose`**, which `foreach` calls for you — and which a
hand-written `while (MoveNext())` loop does not.

**58. A lazy sequence may be infinite.** That is a feature until someone calls `.Count()`.

**59. `Single` throws on two; `First` throws on none; `SingleOrDefault` still throws on two.**
Choosing the wrong one is choosing which bug to have.

**60. `Count()` on an `IEnumerable` may enumerate the whole sequence.** `Any()` stops at the first
element, and against a database it becomes `EXISTS` instead of `COUNT(*)`.

---

## Book VI — Async and concurrency

**61. `async` does not create a thread; it frees one.** An awaiting request occupies no thread at
all. → `dotnet run async`

**62. An `async` method runs synchronously until its first incomplete await.**

**63. Never block on async code: no `.Result`, no `.Wait()`, no `.GetAwaiter().GetResult()`.** It
deadlocks where a synchronization context exists and starves the thread pool where one does not.
→ `dotnet run deadlock`

**64. ASP.NET Core has no synchronization context, so sync-over-async passes every test and
deadlocks in Blazor Server and WPF.** → `dotnet run context`

**65. Library code writes `ConfigureAwait(false)` on every await**, because a library does not know
whose thread it is on.

**66. Pass the `CancellationToken` all the way down.** A token that stops at the handler is
decoration; the point is that the database command receives it.

**67. `async void` is only ever legal for an event handler.** Anywhere else, its exceptions cannot
be caught and take down the process.

**68. The thread pool grows by roughly one thread per second above the core count.** That single
number is the whole mechanism of thread-pool starvation. → `dotnet run threadpool`

**69. p99 latency rising while CPU sits idle means threads, not compute.** The cause is always
blocking.

**70. `counter++` is read-add-write, not one operation.** Two threads lose updates immediately, and
at volume they lose most of them. → `dotnet run race`

**71. `Interlocked` for one variable, `lock` for an invariant across several, a channel for not
sharing at all.** In that order of preference.

**72. `Interlocked.CompareExchange` is optimistic concurrency at the CPU level** — the same
read/compute/conditional-write/retry shape as a `rowversion` check.

**73. Lock on a private readonly object; never on `this`, a `Type`, or a string.** Interned string
literals are shared process-wide, so a stranger can take your lock.

**74. Never `await` inside a lock.** A monitor is owned by a thread and a continuation may resume on
another. `SemaphoreSlim.WaitAsync` is the async critical section.

**75. Take multiple locks in one globally consistent order**, and never call unknown code while
holding one.

**76. Without a fence, another thread may never see your write.** The compiler may hoist it into a
register and the CPU may reorder it. `lock`, `Interlocked` and `volatile` are the fences.

**77. `volatile` does not make `++` atomic.** It orders individual reads and writes; the
read-modify-write between them is still three steps.

**78. `long` and `double` are not atomic on 32-bit, and no multi-word struct ever is.** A torn read
returns a value that never existed.

**79. `Task.WhenAll` throws only the first exception.** The rest are on the task's `Exception`
property, silent unless you look.

**80. `Parallel` and PLINQ are for CPU-bound work only**, the body must be thread-safe, and below a
few thousand items the partitioning costs more than it saves.

**81. A `ValueTask` may be awaited once, never twice, and never concurrently.** It may be a pooled
object that is recycled the instant you read it. → `dotnet run valuetask`

---

## Book VII — The border: LINQ, EF Core and SQL

**82. `IEnumerable` runs here; `IQueryable` runs there.** Assigning one to the other silently moves
the filtering from the database into your process.

**83. `Func<T>` is code; `Expression<Func<T>>` is data.** That difference *is* the difference
between LINQ to Objects and EF Core. → `dotnet run expressions`

**84. A `Where` after an `AsEnumerable()` or a `ToList()` runs in memory.** So does one after any
method the provider cannot translate.

**85. Anything EF cannot translate either throws or — worse in older versions — silently evaluates
on the client.** Read the generated SQL; do not assume it.

**86. N+1 is one query for the parents and one per child collection.** Fix it with a projection or
`Include`. Lazy loading causes it invisibly, which is why it is off here.

**87. Project to a DTO, do not return the entity.** It is a performance rule, a security rule
(module 24) and a contract rule (Book X) simultaneously.

**88. Change tracking is a feature you should turn off for reads.** `AsNoTracking` for anything you
will not modify.

**89. `SaveChanges` is one transaction.** The unit of work is the `DbContext`, not the repository.

**90. A `DbContext` is not thread-safe and is scoped to one request.** Sharing one across
concurrent work is undefined behaviour with a friendly error message.

**91. Equality columns first in an index, then range columns, then sort columns** — and never wrap
an indexed column in a function, which makes the index unusable.

**92. Two users, one row: `rowversion`, 409, retry** — and then a CHECK constraint, because the
database is the only guard that every writer passes.

**93. Pagination without a deterministic `ORDER BY` returns rows twice and skips others.** Order by
something unique, or add the key as a tiebreaker.

**94. A query parameterises values, never identifiers.** Dynamic `ORDER BY` or table names need an
allow list; there is no other safe form.

**95. The isolation level you did not choose is the one you are running.** Know your default, and
know what it permits.

**96. Two systems cannot commit atomically; one row can.** An email, a message or a job written as
a row in the same transaction as the business change lands with it or not at all — and "did we send
it?" becomes a `SELECT`.

**97. A queue table claims its rows; it does not read them.** One statement with `UPDLOCK`,
`READPAST` and `OUTPUT`, or two workers read the same batch and deliver everything twice.

---

## Book VIII — Errors

**98. Expected outcomes are values; bugs and infrastructure failures are exceptions.** "That SKU
does not exist" is an ordinary result of a working system. → `dotnet run exceptions`

**99. Throw for a developer's mistake, return for a user's.** Adding euros to yen is a wiring bug,
not a business outcome.

**100. An exception costs a stack-trace capture and a two-pass unwind** — thousands of times the
cost of returning a value, measurably.

**101. `throw ex` destroys the stack trace; `throw` preserves it.** To rethrow from elsewhere
entirely, `ExceptionDispatchInfo.Capture(ex).Throw()`.

**102. An exception filter (`when`) runs before the stack unwinds**, so the caller's filter runs
*before* the callee's `finally`. That is why filters give better crash dumps than catch-and-rethrow.

**103. Never catch `Exception` except at the outermost boundary**, where you translate it into a
response and log the detail.

**104. An empty catch block is a decision to hide a bug from yourself.**

**105. `.Result` and `.Wait()` wrap exceptions in an `AggregateException`**, so your catch blocks
stop matching.

**106. `finally` always runs, `using` is a `finally`, and neither survives a `StackOverflowException`
or a process kill.** Cleanup that must survive those belongs in the database, not in a `finally`.

**107. A `ProblemDetails` with a correlation id is a response; a stack trace is reconnaissance.**

---

## Book IX — Text, culture and time

**108. Every conversion between text and data makes a culture decision, and the default is
`CurrentCulture`.** Which is almost never what you meant. → `dotnet run culture`

**109. Ordinal for identifiers, Invariant for persistence, Current for humans.** Three categories,
no fourth, and every call site belongs to exactly one.

**110. On an Italian machine, `decimal.Parse("1234.5")` is `12345`.** Silently. No exception, no
log line.

**111. `ToLower()` without a culture fails in Turkish.** Compare with `OrdinalIgnoreCase` rather
than case-folding at all.

**112. Ordinal is both the correct answer and the fast one** for identifiers — it compares memory
rather than walking collation tables.

**113. Anything that becomes CSS, JSON, a URL or SQL is formatted with `InvariantCulture`.**
`width:33,33%` is silently dropped by every browser.

**114. A `char` is a UTF-16 code unit, not a character.** An emoji has `Length == 2`. Never reverse
or truncate by `char` index.

**115. `string` is immutable, so `+=` in a loop is O(n²) in allocation.** → `dotnet run strings`

**116. Store UTC, transmit ISO-8601 with an offset, convert only at the edge.**

**117. `DateTime.Kind` is not persisted by most databases**, so a `Utc` value returns as
`Unspecified` and shifts on the next conversion. Use `DateTimeOffset` for an instant.

**118. An offset is not a time zone.** Only `TimeZoneInfo` knows that 02:30 does not exist one night
in March and happens twice one night in October. → `dotnet run datetime`

**119. `DateTime.UtcNow` is a hidden static dependency.** Inject `TimeProvider`, and month-end and
expiry logic becomes testable.

---

## Book X — Boundaries and contracts

**120. A boundary is a contract: validate crossing in, project crossing out.** Every rule about
DTOs is this one sentence.

**121. Bind to a request DTO, never to an entity.** Over-posting becomes structurally impossible
rather than merely unlikely.

**122. Duplicate the contract across a published boundary.** A rename *should* break the client; if
the client simply uses the server's types there is no contract, only coupling.

**123. `JsonSerializer` serializes the DECLARED type**, so a derived object in a base-typed variable
silently loses its extra properties. → `dotnet run json`

**124. Register one `JsonSerializerOptions` and reuse it.** It caches per-type metadata, and two
instances give you two casing conventions that disagree only at the boundary.

**125. Serialize enums as strings across a published boundary** — and pin the numeric values anyway.

**126. A JWT is signed, not encrypted.** Anyone holding it can read every claim.

**127. `UseAuthentication` before `UseAuthorization`.** Reversed, the principal is empty when
policies run and every protected endpoint returns 401.

**128. Object-level authorization belongs in the handler, next to the data.** An endpoint policy
cannot express "may this user see *this* row".

**129. Every list endpoint has a maximum page size.** An unbounded one is a denial-of-service
parameter with a friendly name.

**130. You cannot atomically write to two systems.** Save-then-publish loses messages;
publish-then-save invents them. Make it one write — the outbox.

**131. A timeout tells you nothing about whether the work happened.** Which is why every retryable
operation must be idempotent.

**132. Exactly-once delivery does not exist.** Choose at-least-once and make the consumer
idempotent.

**133. Retry only idempotent operations, never a 4xx, always with jitter, always with a total time
cap.**

---

## Book XI — Performance

**134. Measure before you optimise, and read the SQL before you touch the C#.** The bottleneck is
almost never where it feels like it is.

**135. Timings from a console app measure the JIT.** Allocation counts are exact; elapsed time
needs BenchmarkDotNet. → `dotnet run boxing`

**136. The JIT compiles per method on first call**, which is why the first request is slow and why
your microbenchmark is lying to you.

**137. Optimise in this order: the algorithm, the round trips, the allocations, the constants.**
Reversing that order is how a week is spent on a 2% improvement.

**138. `Contains` inside a loop over a `List<T>` is O(n²).** Build a `HashSet<T>` first. This is
the most common real performance bug in ordinary business code.

**139. Size a collection you are about to fill.** Growth rehashes or recopies everything, and the
constructor takes the capacity.

**140. `TryGetValue` and `TryAdd` are one lookup; `ContainsKey` plus an indexer is two.**

**141. Dictionary enumeration order is undefined.** It looks like insertion order until the first
removal.

**142. `ConcurrentDictionary` makes each operation atomic, not each sequence** — and `GetOrAdd` may
run your factory more than once, so the factory must be cheap and side-effect free.

**143. `FrozenDictionary` for a lookup table built once and read forever.** Slower to build, fastest
to read.

**144. Reflection is slow because of the metadata lookup and the boxing.** Cache the `MemberInfo`,
build a delegate, and the cost is gone. → `dotnet run reflection`

**145. Prefer a source generator to runtime reflection.** Compile-time work is faster, debuggable,
trimming-safe, and turns runtime failures into build errors.

---

## Book XII — Design

**146. Dependencies point inward. The domain knows nothing about anything.**

**147. Dependency inversion means the high-level module owns the abstraction.** `IOrderRepository`
lives in the Application layer — that placement is the whole idea.

**148. An aggregate is a consistency boundary. Reference other aggregates by id.**

**149. Make illegal states unrepresentable.** A `Sku` and a `Money` cannot be swapped by accident;
two `string` parameters can.

**150. Single responsibility is about reasons to change, not size.** Two stakeholders who can each
demand a change to one class means two responsibilities.

**151. A subtype must be substitutable without the caller knowing**, which is why a member that
throws `NotSupportedException` is a design error and why `sealed` is a good default.

**152. Never expose `IQueryable` from a repository.** The moment you do, the abstraction is
decorative.

**153. Duplicate twice; abstract on the third.** A wrong abstraction costs more than duplication,
because everything gets built on top of it.

**154. A pattern is an answer to a problem you can state.** If you cannot say what goes wrong
without it, you do not need it yet.

**155. An anemic domain model is the default, not a design.** Behaviour belongs next to the data it
protects.

**156. The in-memory provider is not a database.** No constraints, no transactions, no
`rowversion` — it passes tests that production fails.

**157. Warnings are errors.** A warning nobody reads is a warning that does nothing.

---

## Where each book is taught

| Book | Modules |
|---|---|
| I — Types and values | [01](module-01-csharp-advanced/), [22](module-22-clr-internals/) |
| II — Equality and identity | [20](module-20-equality-and-collections/), [01](module-01-csharp-advanced/) |
| III — Null | [01](module-01-csharp-advanced/), [07](module-07-sql-and-transactions/) |
| IV — Memory and lifetime | [19](module-19-memory-and-gc/), [14](module-14-performance/) |
| V — Laziness and evaluation | [03](module-03-linq-internals/), [09](module-09-advanced-linq/) |
| VI — Async and concurrency | [04](module-04-async/), [21](module-21-threading-and-memory-model/) |
| VII — The border | [16](module-16-the-layer-map/), [06](module-06-efcore/), [07](module-07-sql-and-transactions/), [10](module-10-cross-cutting/) |
| VIII — Errors | [05](module-05-clean-architecture/), [10](module-10-cross-cutting/) |
| IX — Text, culture and time | [23](module-23-text-culture-serialization/) |
| X — Boundaries and contracts | [15](module-15-aspnetcore-in-depth/), [24](module-24-security/), [25](module-25-distributed-systems/), [18](module-18-blazor/) |
| XI — Performance | [14](module-14-performance/), [20](module-20-equality-and-collections/), [22](module-22-clr-internals/) |
| XII — Design | [05](module-05-clean-architecture/), [08](module-08-cqrs/), [26](module-26-patterns-and-solid/), [12](module-12-testing/) |
