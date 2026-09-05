# Lab solutions

> Use these after a genuine attempt. Read the **reasoning**, not just the code — an exercise you
> copy teaches nothing; one you struggle with and then compare against teaches you the shape of
> the problem.

Each solution goes into the matching file under `labs/Labs.Exercises/Exercises/`. Then:

```bash
dotnet test labs/Labs.Exercises                                   # all 113
dotnet test labs/Labs.Exercises --filter "FullyQualifiedName~Lab03"   # one lab
```

| Lab | File | Read first |
|---|---|---|
| 02 — LINQ | `Lab02_Linq.cs` | [module 03](module-03-linq-internals/), [module 09](module-09-advanced-linq/) |
| 03 — Async | `Lab03_Async.cs` | [module 04](module-04-async/), [module 21](module-21-threading-and-memory-model/), [module 25](module-25-distributed-systems/) |
| 04 — Equality | `Lab04_Equality.cs` | [module 20](module-20-equality-and-collections/) |
| 05 — Delegates | `Lab05_Delegates.cs` | [module 02](module-02-delegates-and-closures/), [module 01](module-01-csharp-advanced/) |
| 06 — Disposal | `Lab06_Disposal.cs` | [module 19](module-19-memory-and-gc/), [module 21](module-21-threading-and-memory-model/) |
| 07 — Specifications | `Lab07_Specifications.cs` | [module 06](module-06-efcore/), [module 07](module-07-sql-and-transactions/) |

Every solution below is verified against the test suite — they are the code that turns the suite
green, not a sketch of it.

---

## Lab 02 — LINQ

### 1. `HighValueSkus`

```csharp
public static IEnumerable<string> HighValueSkus(IEnumerable<Line> lines, decimal minimumValue) =>
    lines
        .Where(l => l.Quantity * l.UnitPrice > minimumValue)
        .Select(l => l.Sku)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(sku => sku, StringComparer.Ordinal);
```

**Why this order.** `Distinct` before `OrderBy` is both correct and cheaper — sorting fewer items.
The reverse also produces the right answer, because `Distinct` preserves first-seen order and the
input was already sorted, but you would be sorting duplicates you are about to throw away.

**`StringComparer.Ordinal`** matters. The default string comparison is culture-sensitive, so
sorting can differ between machines — the classic Turkish-`I` problem, where `"I".ToLower()` is
`"ı"` and comparisons reorder. For identifiers, always be explicit and ordinal.

---

### 2. `RevenueByCategory`

```csharp
public static IEnumerable<CategoryRevenue> RevenueByCategory(IEnumerable<Line> lines) =>
    lines
        .GroupBy(l => l.Category, StringComparer.Ordinal)
        .Select(g => new CategoryRevenue(
            g.Key,
            g.Sum(l => l.Quantity * l.UnitPrice),
            g.Sum(l => l.Quantity)))
        .OrderByDescending(c => c.Revenue);
```

**Two aggregates over one grouping** — the group is enumerated twice here, which is fine in
memory. Against a database this becomes a single `GROUP BY` computing both in one pass.

**Note this would not translate to SQL if `CategoryRevenue` had a computed property** referencing
something unmapped. See module 09.

---

### 3. `LargestOrderId` — the one everybody gets wrong

```csharp
public static int? LargestOrderId(IEnumerable<Line> lines) =>
    lines
        .GroupBy(l => l.OrderId)
        .Select(g => new { OrderId = g.Key, Total = g.Sum(l => l.Quantity * l.UnitPrice) })
        .MaxBy(x => x.Total)          // returns null for an empty sequence
        ?.OrderId;
```

**The trap.** All of these throw `InvalidOperationException` on an empty sequence:

```csharp
.OrderByDescending(x => x.Total).First()    // ✗
.Max(x => x.Total)                          // ✗
```

`MaxBy` (.NET 6+) returns `null` for a reference/nullable type instead — and it is O(n) with one
pass, where `OrderByDescending().First()` sorts the whole sequence to find one element.

The `?.OrderId` at the end is what propagates the null through to the `int?` return.

**`Max` vs `MaxBy`:** `Max` returns the maximum *value*; `MaxBy` returns the *element* that has
it. Wanting the element and reaching for `Max` is a very common mistake.

---

### 4. `LazySkuQuery` — deferred execution

```csharp
public static IEnumerable<string> LazySkuQuery(
    IEnumerable<Line> lines,
    int minLength,
    Action<string> onEvaluate) =>
    lines
        .Select(l => l.Sku)
        .Where(sku =>
        {
            onEvaluate(sku);          // reports each element as it is examined
            return sku.Length > minLength;
        });
```

**Why no `ToList()`.** Calling it would execute immediately and fail
`does_not_evaluate_anything_until_enumerated`. Returning the query object itself is the whole
point: LINQ operators are lazy, so nothing runs until the caller enumerates.

**Why it re-evaluates.** The returned object holds the source and the predicate, not the results.
Every `foreach` runs the pipeline again — which is why
`re_evaluates_on_every_enumeration` sees the count double.

Against a database that second enumeration is a second round trip. This is the single most common
cause of accidental double-querying.

---

### 5. `ChunkBy` — the two-method iterator pattern

```csharp
public static IEnumerable<IReadOnlyList<T>> ChunkBy<T>(IEnumerable<T> source, int size)
{
    ArgumentNullException.ThrowIfNull(source);
    ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);

    return Iterator(source, size);

    static IEnumerable<IReadOnlyList<T>> Iterator(IEnumerable<T> source, int size)
    {
        List<T> bucket = new(size);

        foreach (T item in source)
        {
            bucket.Add(item);

            if (bucket.Count == size)
            {
                yield return bucket;
                bucket = new List<T>(size);   // a NEW list, not bucket.Clear()
            }
        }

        if (bucket.Count > 0)
        {
            yield return bucket;              // the final partial chunk
        }
    }
}
```

**Three things this gets right, each tested:**

**(a) Eager validation.** A method containing `yield return` does not run *any* of its body until
enumerated. Put the guard inside and `ChunkBy(source, 0)` returns happily, then throws later from
somewhere unrelated. The fix is a normal method that validates and delegates to a private
iterator — this is how essentially every LINQ operator in the BCL is written.

**(b) A new list per chunk, not `Clear()`.** `yield return bucket` hands the caller a *reference*.
Clearing and reusing it means every chunk the caller kept is the same, now-empty list. Subtle,
and it only shows up when someone materialises the chunks.

**(c) Single-pass.** `foreach` over the source exactly once — tested by
`enumerates_the_source_only_once`. An implementation calling `source.Count()` or `Skip/Take` in a
loop would enumerate repeatedly, which is catastrophic for a network stream or a database query.

---

### 6. `OrphanedSkus`

```csharp
public static IEnumerable<string> OrphanedSkus(
    IEnumerable<Line> lines,
    IEnumerable<string> catalogueSkus) =>
    lines
        .Select(l => l.Sku)
        .Distinct(StringComparer.Ordinal)
        .Except(catalogueSkus, StringComparer.Ordinal);
```

**`Except` builds a hash set internally**, so it is O(n + m).

The alternative that looks equivalent and is not:

```csharp
.Where(sku => !catalogueSkus.Contains(sku))   // O(n · m)
```

At 10,000 lines and 10,000 catalogue entries that is a hundred million comparisons instead of
twenty thousand. **This is the single most common accidental-O(n²) in C#**, and it hides well
because it reads perfectly naturally.

The left-outer-join version, for comparison:

```csharp
lines.Select(l => l.Sku).Distinct(StringComparer.Ordinal)
    .GroupJoin(catalogueSkus, sku => sku, c => c, (sku, matches) => new { sku, matches })
    .Where(x => !x.matches.Any())
    .Select(x => x.sku);
```

More general (it can project matched data too) and much noisier. Use `Except` when you only need
set difference.

---

### 7. `CumulativeDailyRevenue`

```csharp
public static IEnumerable<(DateOnly Date, decimal RunningTotal)> CumulativeDailyRevenue(
    IEnumerable<Line> lines)
{
    decimal running = 0m;

    return lines
        .GroupBy(l => l.OrderedOn)
        .OrderBy(g => g.Key)
        .Select(g =>
        {
            running += g.Sum(l => l.Quantity * l.UnitPrice);
            return (g.Key, running);
        })
        .ToList();     // ← materialise, deliberately
}
```

**The `ToList()` is not optional here, and this is the interesting part.**

The `Select` lambda has a **side effect**: it mutates `running`, a captured variable. Combined
with deferred execution, that means enumerating the result twice would keep accumulating —
the second pass starts from wherever the first left off and returns doubled totals.

Materialising once makes the result a snapshot rather than a live query.

The better lesson: **avoid side effects in LINQ lambdas.** A clearer implementation:

```csharp
public static IEnumerable<(DateOnly Date, decimal RunningTotal)> CumulativeDailyRevenue(
    IEnumerable<Line> lines)
{
    List<(DateOnly, decimal)> result = [];
    decimal running = 0m;

    foreach (var group in lines.GroupBy(l => l.OrderedOn).OrderBy(g => g.Key))
    {
        running += group.Sum(l => l.Quantity * l.UnitPrice);
        result.Add((group.Key, running));
    }

    return result;
}
```

Longer, and **better** — the state is obviously local, there is no deferred-execution hazard, and
anyone can read it. Clever LINQ that needs a paragraph of explanation is a liability in code
someone else maintains.

---

## Lab 03 — Async

> Every one of these is verified against the test suite. Read the **commentary**, not just the
> code — three of the four have a subtlety that the obvious implementation gets wrong.

### 1. `WithTimeoutAsync`

```csharp
public static async Task<T> WithTimeoutAsync<T>(
    Task<T> task,
    TimeSpan timeout,
    CancellationToken cancellationToken = default)
{
    cancellationToken.ThrowIfCancellationRequested();

    using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

    Task delay = Task.Delay(timeout, timer.Token);
    Task first = await Task.WhenAny(task, delay).ConfigureAwait(false);

    if (first == task)
    {
        await timer.CancelAsync().ConfigureAwait(false);   // stop the timer; do not leak it
        return await task.ConfigureAwait(false);           // rethrows the ORIGINAL exception
    }

    cancellationToken.ThrowIfCancellationRequested();      // cancellation beats timeout
    throw new TimeoutException($"The operation timed out after {timeout}.");
}
```

**`return await task` rather than `task.Result`.** This is the whole reason the "unwrapped
exception" test exists. `await` rethrows the original exception; `.Result` wraps it in an
`AggregateException`, so the caller's `catch (InvalidOperationException)` stops matching. And
`.Result` would block a thread on a task you have already proven is complete — pointless, and
exactly the habit module 21 is trying to break.

**Cancelling the timer is not tidiness.** Without it, every fast call leaves a `Task.Delay` timer
armed for the full timeout. At a thousand requests a second with a 30-second timeout, that is
30,000 live timers holding 30,000 continuations. It is a real leak, and it looks like a slow one.

**`ThrowIfCancellationRequested` before the `TimeoutException`** is what makes the third test
pass. The linked source fires the delay when *either* the timeout elapses or the caller cancels,
so on the losing branch you have to ask which one it was — and the caller's cancellation is the
more truthful answer.

**The thing worth noticing.** After a timeout, the original task is still running. You have not
cancelled it; you have abandoned it. If it later faults, nobody observes that exception. This is
why real cancellation means passing the token *into* the work rather than racing it from outside
— which is the point module 04 makes about threading the token all the way down.

---

### 2. `RetryAsync`

```csharp
public static async Task<T> RetryAsync<T>(
    Func<CancellationToken, Task<T>> operation,
    int maxAttempts,
    Func<Exception, bool> shouldRetry,
    Func<int, TimeSpan> delay,
    CancellationToken cancellationToken = default)
{
    ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);

    ExceptionDispatchInfo? last = null;

    for (int attempt = 1; attempt <= maxAttempts; attempt++)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return await operation(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;                       // the caller asked to stop; never retry that
        }
        catch (Exception ex) when (shouldRetry(ex))
        {
            last = ExceptionDispatchInfo.Capture(ex);
        }

        if (attempt < maxAttempts)
        {
            TimeSpan wait = delay(attempt);
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    last!.Throw();                       // preserves the original stack trace
    throw new UnreachableException();
}
```

**`ExceptionDispatchInfo`, not `throw last`.** Storing an exception and rethrowing it with
`throw last;` resets its stack trace to that line — the `exceptions` demo shows exactly this. The
retry loop is one of the few places you legitimately need to rethrow an exception you caught
earlier, and `ExceptionDispatchInfo.Capture(ex).Throw()` is the tool for it.

**Two catch clauses, and the order matters.** The cancellation clause must come first. Without it,
a `shouldRetry: _ => true` predicate happily retries the caller's own cancellation, which is both
wrong and hard to notice.

**`catch (Exception ex) when (shouldRetry(ex))`, not an `if` inside the catch.** With the filter,
a non-retryable exception is never caught at all, so it propagates with its stack frames intact
(module 24's point about filters and the two-pass unwind). With an `if` and a bare `throw;` you
would get the same result here, but the filter states the intent in the signature.

**`delay` is a parameter, and that is the design lesson.** The tests pass `_ => TimeSpan.Zero` and
run in milliseconds; production passes exponential backoff with jitter. A retry helper with a
hard-coded `Task.Delay(1000 * attempt)` is a helper whose tests take a minute, so nobody writes
them.

**In real code you would use `Microsoft.Extensions.Http.Resilience`.** Write it once by hand so
that when the standard handler misbehaves you know what it is doing.

---

### 3. `MapWithConcurrencyLimitAsync`

```csharp
public static async Task<IReadOnlyList<TOut>> MapWithConcurrencyLimitAsync<TIn, TOut>(
    IEnumerable<TIn> source,
    Func<TIn, CancellationToken, Task<TOut>> operation,
    int maxConcurrency,
    CancellationToken cancellationToken = default)
{
    ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrency, 1);

    TIn[] items = [.. source];
    if (items.Length == 0)
    {
        return [];
    }

    using var gate = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    Task<TOut>[] tasks = new Task<TOut>[items.Length];

    for (int i = 0; i < items.Length; i++)
    {
        TIn item = items[i];             // copy — see the closure trap, module 02
        tasks[i] = RunAsync(item);
    }

    return await Task.WhenAll(tasks).ConfigureAwait(false);

    async Task<TOut> RunAsync(TIn item)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await operation(item, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();              // MUST be in a finally, or the limit shrinks
        }
    }
}
```

**`Release()` in a `finally` is the entire exercise.** Put it after the `await` instead and every
failing item permanently consumes one unit of the semaphore. After `maxConcurrency` failures the
whole method blocks forever — and that test would *hang* rather than fail, which is why it is
written with a `Task.WhenAny` timeout. That failure mode is a genuinely nasty production bug: it
needs failures to trigger, so it appears weeks later, under load, and never on your machine.

**Ordering comes free from the array.** Fill `tasks[i]` by index and `Task.WhenAll` returns
results in that same order regardless of completion order. Anyone reaching for a
`ConcurrentBag` here has made the problem harder than it is.

**`TIn item = items[i]` inside the loop.** A `for` loop still captures one shared variable — the
trap from module 02, which `foreach` was changed to avoid in C# 5 and `for` was not. Here the
local is genuinely needed.

**`await gate.WaitAsync(...)`, never `gate.Wait()`.** The synchronous version blocks a pool
thread while waiting for a slot, which turns a concurrency limiter into a thread-pool starvation
machine — the exact opposite of the point.

**`Parallel.ForEachAsync` does this for you** and is the right answer in production code. Writing
it once by hand is how you know what to expect from it.

---

### 4. `WhenAllCollectingErrorsAsync`

```csharp
public static async Task<IReadOnlyList<T>> WhenAllCollectingErrorsAsync<T>(
    IEnumerable<Task<T>> tasks)
{
    Task<T>[] all = [.. tasks];
    Task<T[]> combined = Task.WhenAll(all);

    try
    {
        return await combined.ConfigureAwait(false);
    }
    catch (Exception)
    {
        // `await` threw only the FIRST exception. The combined task holds them all.
        throw combined.Exception ?? new AggregateException();
    }
}
```

**Await the combined task, then read its `Exception`.** `await Task.WhenAll(...)` unwraps the
`AggregateException` and rethrows only the first inner exception — a convenience that quietly
discards the rest. The combined task still holds every one of them on its `Exception` property,
which is what the catch block reaches for.

**Await the combined task rather than looping over the individual ones.** `Task.WhenAll` waits for
all of them regardless of failures, so nothing is left unobserved. A loop that awaits each task in
turn would throw at the first failure and abandon the others — and an unobserved faulted task is
a warning at best and a process-level event at worst.

**`[.. tasks]` materialises the sequence first.** If the caller passed a LINQ query that *starts*
each task, enumerating it twice would start every task twice. That is the deferred-execution rule
from module 03 meeting the async rules from module 04, and the collision is easy to miss.

---

## Lab 04 — Equality and collections

### 1. `StockKey`

```csharp
public bool Equals(StockKey other) =>
    WarehouseId == other.WarehouseId
    && string.Equals(Sku, other.Sku, StringComparison.OrdinalIgnoreCase);

public override bool Equals(object? obj) => obj is StockKey other && Equals(other);

public override int GetHashCode() =>
    HashCode.Combine(
        StringComparer.OrdinalIgnoreCase.GetHashCode(Sku),
        WarehouseId);
```

**`IEquatable<StockKey>` is what makes the no-allocation test pass.** Without the typed overload,
every comparison goes through `Equals(object?)`, which boxes both operands — 48 bytes a
comparison, on the hot path of every dictionary lookup. The `equality` demo measures it.

**Compare the `int` first.** It is one instruction; the string comparison is a loop. Short-circuit
evaluation means most unequal keys never touch the string at all.

**`StringComparer.OrdinalIgnoreCase.GetHashCode(Sku)`, not `Sku.ToLower().GetHashCode()`.** Two
independent reasons, and the exercise asks for both:

1. `ToLower()` **allocates a new string on every hash**, which is a per-lookup allocation in the
   hottest path a dictionary has.
2. `ToLower()` without a culture is culture-sensitive, so on a Turkish machine `"ELE-1"` and
   `"ele-1"` case-fold differently and stop hashing equally — the contract breaks, and your
   dictionary starts losing entries in one country only. Module 23.

**`HashCode.Combine`, not XOR.** XOR is commutative, so `("A", 66)` and `("B", 65)` would collide
if you combined the string's hash with the id by XOR. Collisions are legal but they are pure cost,
and in a composite key they are not rare — they are systematic.

**The consistency the test checks:** equal keys hash equally. Everything else about the type
follows from that one requirement.

---

### 2. `ByUnitsDescendingThenName`

```csharp
private sealed class HoldingComparer : IComparer<Holding>
{
    public int Compare(Holding? x, Holding? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return 1;
        if (y is null) return -1;

        int byUnits = y.Units.CompareTo(x.Units);       // y first == descending
        return byUnits != 0 ? byUnits : string.CompareOrdinal(x.Name, y.Name);
    }
}

public static IComparer<Holding> ByUnitsDescendingThenName() => new HoldingComparer();
```

**`y.Units.CompareTo(x.Units)`, never `y.Units - x.Units`.** The subtraction is the exercise. With
`int.MaxValue` and `int.MinValue` it overflows, wraps, and **reverses the sign** — so the comparer
confidently reports that the largest holding is the smallest. `List.Sort` does not validate its
comparer, so you get a silently wrong order rather than an exception. Every "why is this list
sorted backwards, but only sometimes" bug is this.

**Swapping the operands is how you express descending.** Negating the result (`-x.CompareTo(y)`)
looks equivalent and is not: `CompareTo` may return `int.MinValue`, and `-int.MinValue` is
`int.MinValue`. The same overflow, one level up.

**`string.CompareOrdinal`, not `string.Compare`.** These are warehouse names used as a
deterministic tiebreaker, not text shown to a user in their own language — so it must sort the
same on every machine. Module 23's three-way rule: this is an identifier, so it is ordinal.

**Why a tiebreaker at all?** Without one, two holdings with equal units have an unspecified
relative order, and `List.Sort` is an *unstable* sort — so the same data can come out in different
orders on different runs. It is the same problem as pagination without a deterministic `ORDER BY`
(module 07), and it has the same fix.

---

### 3. `MissingSkus`

```csharp
public static IReadOnlyList<string> MissingSkus(
    IEnumerable<string> ordered,
    IEnumerable<string> inStock)
{
    var held = inStock.ToHashSet(StringComparer.OrdinalIgnoreCase);
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var missing = new List<string>();

    foreach (string sku in ordered)
    {
        if (!held.Contains(sku) && seen.Add(sku))
        {
            missing.Add(sku);
        }
    }

    return missing;
}
```

**One `HashSet` for membership, one for de-duplication.** The naive version —
`ordered.Where(s => !inStock.Contains(s))` — is O(n×m). At the test's 20,000 × 20,000 that is 400
million string comparisons and takes seconds; the set version does 40,000 hash lookups and takes
milliseconds. This is the most common real performance bug in ordinary business code, and it only
appears once the data grows.

**`seen.Add(sku)` returns `false` if it was already there**, so the membership check and the
insert are one operation and one lookup. Writing `if (!seen.Contains(sku)) { seen.Add(sku); ... }`
does the same work twice — the same rule as `TryGetValue` over `ContainsKey` plus an indexer.

**Both comparers must be `OrdinalIgnoreCase`.** If de-duplication were case-sensitive, `"ELE-9"`
and `"ele-9"` would both appear in the output while `held` treated them as one — two collections
disagreeing about what "the same" means, which is Book II of the laws in miniature.

**A `foreach` rather than LINQ, deliberately.** The LINQ version needs a closure over both sets
and a `Distinct` with a comparer, and it reads worse. Clever LINQ that needs a paragraph of
explanation is a liability.

---

### 4. `MutateKeyInPlace`

```csharp
public static (bool Found, int Count) MutateKeyInPlace(MutableStockKey key, string newSku)
{
    var dictionary = new Dictionary<MutableStockKey, string> { [key] = "Wireless Scanner" };
    key.Sku = newSku;
    return (dictionary.ContainsKey(key), dictionary.Count);
}
```

Four obvious lines, and the answer is `(false, 1)`.

**The entry is in the bucket for the OLD hash.** `ContainsKey` computes the *new* hash, goes to a
different bucket, and finds nothing. The entry is still there — `Count` proves it — and it is now
**unreachable and unremovable**: `Remove(key)` looks in the new bucket too, so even deliberate
cleanup fails.

**There is no fix inside this method.** You cannot re-hash the dictionary, because `Dictionary<K,V>`
has no API for it and no way to know the key changed. The only fix is upstream: **dictionary keys
are immutable.** That is why `StockKey` in exercise 1 is a `readonly struct` with get-only
properties, and why the strongly-typed ids in the domain are `readonly record struct`.

**Why this matters more than it looks.** The broken version compiles, passes review, and works
perfectly until someone mutates a key that happens to be in a cache. Then a single entry is lost,
the cache grows without bound, and nothing anywhere reports an error.

---

## Lab 05 — Delegates, generics and events

### 1. `MyWhere`

```csharp
public static IEnumerable<T> MyWhere<T>(IEnumerable<T> source, Func<T, bool> predicate)
{
    ArgumentNullException.ThrowIfNull(source);
    ArgumentNullException.ThrowIfNull(predicate);
    return Iterate(source, predicate);

    static IEnumerable<T> Iterate(IEnumerable<T> source, Func<T, bool> predicate)
    {
        foreach (T item in source)
        {
            if (predicate(item))
            {
                yield return item;
            }
        }
    }
}
```

**The two-method pattern is the whole exercise.** A method containing `yield return` is compiled
into a state machine, and *none* of its body runs until somebody calls `MoveNext`. Put the null
checks in the iterator and they fire on first enumeration — which may be in a different method, a
different layer, or a `foreach` in a Razor view, a long way from the actual bug. Splitting it means
the argument validation is eager and only the iteration is lazy. `Enumerable.Where` does exactly
this, and being able to say so is a good interview answer.

**`yield return` is what makes it stream.** The infinite-sequence test is not a curiosity: it is the
proof that nothing is buffered. A `ToList()` anywhere in this method would hang that test forever
rather than fail it.

---

### 2. `Memoize`

```csharp
public static Func<TIn, TOut> Memoize<TIn, TOut>(Func<TIn, TOut> function)
    where TIn : notnull
{
    ArgumentNullException.ThrowIfNull(function);
    Dictionary<TIn, TOut> cache = [];

    return argument =>
    {
        if (cache.TryGetValue(argument, out TOut? cached))
        {
            return cached;
        }

        TOut result = function(argument);
        cache[argument] = result;
        return result;
    };
}
```

**`TryGetValue`, not `ContainsKey` and then the indexer.** Two lookups where one will do, and the
one-lookup version is also what makes the cached-`null` test pass: `ContainsKey` and "is the value
null" are different questions, and conflating them turns a cached `null` into a permanent cache miss
that calls your expensive function on every request.

**The dictionary is a local that outlives the method.** That is the closure. The compiler hoists it
into a generated class and the returned lambda holds a reference to that instance — which is why two
calls to `Memoize` get two independent caches, and why the captured dictionary is not
garbage-collected while the returned function is alive. Module 02 shows the generated class.

**This is not thread-safe, deliberately.** Two threads can both miss, both compute, and one write can
corrupt the dictionary's internal state — `Dictionary` is explicitly not safe for concurrent writes.
Lab 06 exercise 4 is this problem solved properly. It is worth noticing that the fix is not "add a
lock": it is deciding whether you want one loader or many.

---

### 3. `MakeCounters`

```csharp
public static IReadOnlyList<Func<int>> MakeCounters(int count)
{
    List<Func<int>> counters = new(count);

    for (int i = 0; i < count; i++)
    {
        int captured = i;
        counters.Add(() => captured);
    }

    return counters;
}
```

**One extra line, and it is the whole bug.** A closure captures the *variable*, not its value. A
`for` loop has exactly one `i` for the whole loop, so without `captured` all three lambdas share it
and all three see `3` after the loop ends. Declaring `captured` inside the body gives each iteration
its own variable, so each lambda closes over a different one.

**Why `foreach` does not have this problem and `for` still does.** C# 5 changed `foreach` so the
iteration variable is a fresh variable per iteration — a breaking change, made because the old
behaviour was almost never what anyone wanted. `for` was left alone, correctly: its variable is one
you declared, initialised and mutate yourself, and "resetting" it per iteration would be
meaningless. So the inconsistency is real and it is the right one.

---

### 4. `Largest`

```csharp
public static T Largest<T>(IEnumerable<T> source)
    where T : IComparable<T>
{
    ArgumentNullException.ThrowIfNull(source);

    using IEnumerator<T> enumerator = source.GetEnumerator();

    if (!enumerator.MoveNext())
    {
        throw new InvalidOperationException("Sequence contains no elements.");
    }

    T best = enumerator.Current;

    while (enumerator.MoveNext())
    {
        if (enumerator.Current.CompareTo(best) > 0)
        {
            best = enumerator.Current;
        }
    }

    return best;
}
```

**The constraint is what makes `CompareTo` legal.** Without `where T : IComparable<T>` the method
does not compile, because an unconstrained `T` is only known to be an `object`. That is what
constraints are *for*: they buy back the capabilities that genericity gave away.

**Drive the enumerator by hand rather than `foreach` with a flag.** It reads better than
`bool first = true`, and it makes the single-enumeration guarantee obvious — which matters because
the source may be a database query, and enumerating it twice means executing it twice.

**Throwing on empty rather than returning `default`.** For `int`, `default` is `0`, which is a
plausible-looking answer and therefore worse than an exception. This is the same trap as `Max()` on
an empty sequence in Lab 02: a wrong number that nobody notices beats a crash only from the
program's point of view, never from the business's.

---

### 5. `StockWatcher`

```csharp
public event EventHandler<StockLowEventArgs>? StockLow;

public void Report(string sku, int remaining, int threshold)
{
    if (remaining < threshold)
    {
        StockLow?.Invoke(this, new StockLowEventArgs(sku, remaining));
    }
}
```

**The `event` keyword is the entire first test.** Without it you have a public delegate field, and
the compiler will happily let any caller do two things they must not: raise the event on your
behalf, and assign with `=` — which silently discards every subscriber anyone else registered. With
`event`, the only operations exposed outside the declaring type are `+=` and `-=`. That is
encapsulation applied to a delegate, and it is the answer to "what is the difference between a
delegate and an event".

**`?.Invoke` rather than a null check.** An event with no subscribers *is* `null`, so a bare
`StockLow(this, args)` throws. The older `if (StockLow != null) StockLow(...)` also has a race — a
subscriber can unsubscribe between the check and the call — while `?.` reads the field once into a
temporary and is therefore safe.

**The reference direction is what causes the leak.** Subscribing makes the *publisher* hold the
*subscriber*. A long-lived `StockWatcher` and a screen that subscribes and is then closed means the
screen can never be collected, and neither can anything it references. In a web request that lives
40 ms nobody notices; in a WPF process that runs from 07:00 until the operator goes home, it is
three gigabytes by the afternoon. Pair every `+=` with a `-=`.

---

## Lab 06 — Disposal, lifetime and shared state

### 1. `Lease` and `LeasePool`

```csharp
public Lease Take()
{
    if (Available == 0)
    {
        throw new InvalidOperationException("The pool is exhausted.");
    }

    Available--;
    return new Lease(this);
}

// on Lease:
private bool _disposed;

public int Use()
{
    ObjectDisposedException.ThrowIf(_disposed, this);
    return 1;
}

public void Dispose()
{
    if (_disposed)
    {
        return;
    }

    _disposed = true;
    _pool.Return();
}
```

**Idempotence is a requirement of the interface, not a nicety.** The documented contract for
`IDisposable` is that calling `Dispose` more than once must be safe, and the reason is that it
happens constantly: a `using` block around an object something else also disposes, a `finally` plus
an explicit call, a class that disposes its fields being disposed twice itself. Here a second call
would return the lease twice and the pool would report more capacity than it has — which surfaces
much later, as a pool handing out more connections than the database allows.

**Set the flag before doing the work,** not after. If `Return()` threw, a flag set afterwards would
leave the object able to try again.

**Failing loudly after disposal.** `ObjectDisposedException.ThrowIf` exists precisely for this and
reads better than a hand-written throw. The alternative — quietly working anyway — means operating on
a resource that now belongs to somebody else, which is how two callers end up sharing one
connection.

**Why no finalizer.** There is no *unmanaged* resource here. A finalizer on a type that does not need
one is a real cost: the object goes on the finalizer queue at allocation, survives at least one extra
collection, and is not freed until the finalizer thread gets to it. Write one only when you directly
hold a native handle — and then still implement `IDisposable` so the normal path never needs it.

---

### 2. `AsyncBatchWriter`

```csharp
private readonly List<string> _buffer = [];
private bool _disposed;

public void Add(string item)
{
    ObjectDisposedException.ThrowIf(_disposed, this);
    _buffer.Add(item);
}

public async ValueTask DisposeAsync()
{
    if (_disposed)
    {
        return;
    }

    _disposed = true;

    if (_buffer.Count > 0)
    {
        await flush(_buffer);
    }
}
```

**`IAsyncDisposable`, because the release does I/O.** A synchronous `Dispose` that blocked on a
network write would occupy a thread pool thread for the whole round trip — the thread-starvation
problem from module 04 arriving through a side door. If a type offers both, prefer `await using`.

**`await using` compiles to `try/finally`,** which is why the flush still happens when the body
throws. That test is not really about disposal; it is about understanding what the keyword expands
to.

**Not flushing an empty buffer.** A batch write of nothing is a network round trip that accomplishes
nothing, and at scale that is a measurable share of your I/O.

---

### 3. `SafeCounter`

```csharp
private long _count;

public long Value => Interlocked.Read(ref _count);

public void Increment() => Interlocked.Increment(ref _count);
```

**`_count++` is three operations.** Read, add, write. Two threads can both read `10`, both add one,
and both write `11` — one increment vanishes. Nothing about either thread is wrong; the mistake was
treating a read-modify-write as atomic.

**`Interlocked` rather than `lock`,** for a single value. It compiles to one atomic CPU instruction
with no kernel involvement and no chance of deadlock, and it is dramatically cheaper under contention
than acquiring a monitor. `lock` becomes the right answer the moment the critical section covers
*more than one* field, because then the whole update has to be atomic, not each field individually.

**`Interlocked.Read` for the getter, and this surprises people.** A plain read of a `long` is not
guaranteed atomic on a 32-bit runtime — it is two 32-bit reads, and a concurrent write between them
gives you a value that never existed. On x64 it happens to be atomic, which is exactly what makes
this the kind of bug that appears on one deployment target only.

**Run it wrong first.** The failure prints a different number every time, and that irreproducibility
is the real lesson: it is why race conditions survive code review, survive testing, and get closed as
"cannot reproduce".

---

### 4. `SingleFlightCache`

```csharp
private readonly ConcurrentDictionary<TKey, TValue> _values = new();
private readonly ConcurrentDictionary<TKey, SemaphoreSlim> _gates = new();

public async Task<TValue> GetAsync(TKey key)
{
    if (_values.TryGetValue(key, out TValue? cached))
    {
        return cached;
    }

    SemaphoreSlim gate = _gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
    await gate.WaitAsync();

    try
    {
        // Double-check: somebody may have loaded it while we queued.
        if (_values.TryGetValue(key, out cached))
        {
            return cached;
        }

        TValue value = await load(key);
        _values[key] = value;
        return value;
    }
    finally
    {
        gate.Release();
    }
}
```

**You cannot `await` inside a `lock`, and it does not compile.** A monitor is owned by a *thread*, and
the continuation after an `await` may resume on a different one, so releasing it would be
meaningless. `SemaphoreSlim` is thread-agnostic: whoever calls `Release` releases it. That is the
whole reason it exists.

**`Release()` in a `finally`, always.** If `load` throws and you release on the happy path only, the
gate is held forever and every subsequent caller for that key waits for a lock nobody will ever give
back. That is a hang, not a crash, which makes it much harder to diagnose.

**One gate per key, not one gate for the cache.** A single semaphore would make a slow load of key A
block every request for key B — you would have solved the stampede by serialising the whole cache.

**The double-check inside the gate is not redundant.** Fifty callers miss, one wins the semaphore and
loads; the other forty-nine then acquire it one at a time and must find the loaded value rather than
load again. Without that second check you get fifty loads with extra steps.

**Why not `ConcurrentDictionary<TKey, Task<TValue>>.GetOrAdd`?** It is shorter, and it is what a lot
of production code does. But a faulted `Task` is a perfectly good cached value, so one transient
failure is cached permanently and every later caller gets the same stale exception. If you use that
form you must remove the entry on failure — and at that point it is no shorter. The test for that
requirement exists precisely to catch the elegant wrong answer.

---

## Lab 07 — Specifications and keyset pagination

### 1. `And`, `Or`, `Not`

```csharp
public static Expression<Func<T, bool>> And<T>(
    Expression<Func<T, bool>> left,
    Expression<Func<T, bool>> right) =>
    Combine(left, right, Expression.AndAlso);

public static Expression<Func<T, bool>> Or<T>(
    Expression<Func<T, bool>> left,
    Expression<Func<T, bool>> right) =>
    Combine(left, right, Expression.OrElse);

public static Expression<Func<T, bool>> Not<T>(Expression<Func<T, bool>> predicate) =>
    Expression.Lambda<Func<T, bool>>(
        Expression.Not(predicate.Body),
        predicate.Parameters[0]);

private static Expression<Func<T, bool>> Combine<T>(
    Expression<Func<T, bool>> left,
    Expression<Func<T, bool>> right,
    Func<Expression, Expression, BinaryExpression> join)
{
    ParameterExpression parameter = left.Parameters[0];
    Expression rewritten = new ParameterReplacer(right.Parameters[0], parameter).Visit(right.Body);

    return Expression.Lambda<Func<T, bool>>(join(left.Body, rewritten), parameter);
}

private sealed class ParameterReplacer(ParameterExpression from, ParameterExpression to)
    : ExpressionVisitor
{
    protected override Expression VisitParameter(ParameterExpression node) =>
        node == from ? to : base.VisitParameter(node);
}
```

**The parameter rewrite is the difficulty and the point.** `p => p.Price >= 100` and
`p => !p.Discontinued` each have their own `ParameterExpression`. They print identically and they are
different objects, so a tree built from both bodies would reference a parameter its own lambda does
not declare — and blow up at `Compile`, or at translation, with a message about a parameter not being
bound. `ExpressionVisitor` is the framework's own tool for rewriting a tree, and this eight-line
visitor is the standard solution.

**Why `x => left.Compile()(x) && right.Compile()(x)` is the wrong answer** even though it passes
every behavioural test: the resulting tree contains an `Invoke` of a compiled delegate, and EF Core
cannot translate that to SQL. Depending on provider and version you get either a translation
exception or — much worse — silent client-side evaluation, which loads the whole table into memory and
filters it there. That is the `IEnumerable` versus `IQueryable` distinction from module 03 with a
production incident attached, and it is why the test asserts on `NodeType` rather than on results.

**`AndAlso`, not `And`.** `AndAlso` is short-circuiting `&&`; `And` is bitwise `&` and evaluates both
sides. In SQL translation the difference is usually invisible; in a compiled delegate it is the
difference between skipping an expensive second predicate and always running it.

---

### 2. `Cursor`

```csharp
public string Encode() =>
    Base64Url.EncodeToString(Encoding.UTF8.GetBytes($"{CreatedAt:O}|{Id:D}"));

public static bool TryDecode(string? token, out Cursor cursor)
{
    cursor = default;

    if (string.IsNullOrWhiteSpace(token))
    {
        return false;
    }

    byte[] bytes;
    try
    {
        bytes = Base64Url.DecodeFromChars(token);
    }
    catch (FormatException)
    {
        return false;
    }

    string[] parts = Encoding.UTF8.GetString(bytes).Split('|');

    if (parts.Length != 2
        || !DateTimeOffset.TryParseExact(
                parts[0], "O", null, DateTimeStyles.RoundtripKind, out DateTimeOffset createdAt)
        || !Guid.TryParse(parts[1], out Guid id))
    {
        return false;
    }

    cursor = new Cursor(createdAt, id);
    return true;
}
```

**The `"O"` format specifier is not decoration.** It is the round-trip format: full sub-second
precision and the UTC offset. Any shorter format silently truncates, and a cursor whose timestamp is
rounded to the second will skip every row sharing that second — a data-loss bug that only appears
under load, when rows start sharing timestamps.

**`TryDecode`, never `Decode`.** This value arrives in a URL, which means it arrives from users,
crawlers, someone's bookmark from three releases ago, and someone else editing it by hand to see what
happens. A malformed cursor must be a `false`, not an unhandled exception and a 500. Note that
`Base64Url.DecodeFromChars` throws on invalid input, so the `try` is doing real work — this is one of
the cases where catching a specific exception *is* the Try pattern.

**Opaque is not secure.** Anyone can decode this and read the timestamp. That is completely fine for a
cursor — it carries no secret — and would not be fine for anything that did. Encoding is not
encryption; the site's chapter 15 makes the same point about JWTs.

---

### 3. `KeysetPage`

```csharp
public static Page<Row> KeysetPage(IEnumerable<Row> source, int pageSize, string? afterToken = null)
{
    ArgumentNullException.ThrowIfNull(source);
    ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pageSize, 0);

    IEnumerable<Row> ordered = source
        .OrderByDescending(r => r.CreatedAt)
        .ThenByDescending(r => r.Id);

    if (Cursor.TryDecode(afterToken, out Cursor after))
    {
        // "Strictly after" in a compound ordering is a compound comparison.
        ordered = ordered.Where(r =>
            r.CreatedAt < after.CreatedAt
            || (r.CreatedAt == after.CreatedAt && r.Id.CompareTo(after.Id) < 0));
    }

    // Take one extra row: its existence is how we know whether there is a next page,
    // without a second COUNT query.
    List<Row> window = [.. ordered.Take(pageSize + 1)];
    bool hasMore = window.Count > pageSize;

    if (hasMore)
    {
        window.RemoveAt(window.Count - 1);
    }

    string? next = hasMore
        ? new Cursor(window[^1].CreatedAt, window[^1].Id).Encode()
        : null;

    return new Page<Row>(window, next);
}
```

**The tie-breaker is not optional.** `CreatedAt` is not unique, so ordering by it alone is not a
*total* order — the database is free to return equal-timestamped rows in any order, and it will return
them in different orders on different executions. Add a unique column and the ordering becomes
deterministic, which is the precondition for a cursor meaning anything at all.

**"Strictly after" is a compound comparison, not `CreatedAt < cursor.CreatedAt`.** Rows sharing the
cursor's timestamp with a smaller id have not been returned yet, and the naive version skips all of
them. In SQL this is the row-value comparison `(CreatedAt, Id) < (@at, @id)`, and getting it wrong
loses rows silently — the worst failure mode a pagination bug can have.

**Fetching `pageSize + 1` answers "is there a next page" for free.** The alternative is a second
`COUNT(*)` over the same predicate, which on a large table costs more than the page itself. The extra
row is discarded; only its existence is used. It is also why an exactly-full last page correctly
reports no next cursor instead of handing the caller an empty page.

**Why not `Skip(n).Take(m)`.** `OFFSET` counts rows at read time, so anything inserted or deleted
while a caller is paging shifts every later page. The test inserts a row between page one and page
two and asserts that nothing is repeated and nothing is skipped — with `Skip`/`Take` that test fails,
and in production it means a customer exporting a report gets one order twice and never sees another.
It is also slow: `OFFSET 100000` makes the database read and discard a hundred thousand rows before
returning anything, while a keyset query seeks straight into the index.

---

## Going further

The labs now cover LINQ, async, equality, delegates, disposal and query composition. The rest of
the course is exercised by the codebase itself. If you want more
practice, these are ordered roughly by difficulty:

1. **Add `GET /api/orders/{id}/shipments`** — a query, a handler, an endpoint, an integration
   test. Touches every layer once.
2. **Add a `Bronze` tier** to `CustomerTier`. Note that the build fails until you handle it in
   both switch expressions — that is the exhaustiveness safety net from module 01 doing its job.
3. **Convert `SendConfirmationOnOrderSubmitted` to use the outbox** instead of sending inside the
   transaction. The flaw is deliberate and labelled; module 06 explains the fix, and `IEmailQueue`
   plus a template in `Application/Mailing/OrderEmails.cs` is all the machinery you need —
   `SendCancellationNoticeOnOrderCancelled` is the worked example to copy.
4. **Add returns and refunds** — a new aggregate, a state machine, domain events, a read model,
   endpoints, and tests at all three levels. If you can do this without referring back, you know
   this material.
