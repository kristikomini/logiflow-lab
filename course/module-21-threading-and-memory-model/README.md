# Module 21 — Threading, locks, and the memory model

> Module 04 taught you `async`, which is about **waiting** efficiently. This module is the layer
> underneath: what happens when two threads genuinely run **at the same time**, on different
> cores, over the same memory. Most .NET developers never learn it, and it is exactly what
> separates "writes web APIs" from "can be trusted with the background worker".

```bash
cd labs/Labs.Playground
dotnet run race          # a lost update, and the three ways to stop it
dotnet run context       # SynchronizationContext, and ConfigureAwait(false)
dotnet run deadlock      # sync-over-async, reproduced safely
dotnet run threadpool    # starvation, watched live
```

---

## 1. A thread is not a task, and neither is a core

```
   CORE          real parallelism. You have 8 or 16. That is your hard ceiling.
   THREAD        an OS scheduling unit. ~1 MB of stack. Expensive to create (~1 ms).
   THREAD POOL   a managed set of reusable threads, sized around the core count.
   TASK          a PROMISE OF A RESULT. Not a thread. Often no thread at all.
```

`Task.Run(...)` queues work **to the pool**. `await someIo` occupies **no thread whatsoever**
while it waits. This is the distinction module 04 makes and it is worth repeating here from the
other side: threads are the scarce physical resource, tasks are the cheap logical one.

**When to create a real `Thread` yourself:** essentially never in a server application. The
legitimate cases are a long-running dedicated loop that must not consume a pool thread, and a
thread that needs a non-default priority, stack size, or apartment state. Everything else is
`Task.Run` or a `BackgroundService`.

**The thread pool's injection rate is the whole story of starvation.** Up to `ProcessorCount`
threads are available instantly. Beyond that, the pool adds roughly **one thread per second**
while it watches throughput. So:

```
   32 requests arrive. Each blocks a thread for 100 ms.
   16 run. 16 queue. The pool adds one thread per second to catch up.
   Meanwhile more requests arrive.
   → latency climbs, CPU sits near idle, and it looks like "the database is slow".
```

`dotnet run threadpool` shows the two versions side by side. The symptom to recognise: **p99
latency rising while CPU is low**. That combination means threads, not compute.

---

## 2. The lost update

```csharp
_counter++;     // NOT one operation
```

It is three: read, add, write. Two threads read 41, both add one, both write 42. One increment
vanished. `dotnet run race` loses roughly **75% of 800,000 increments** on a typical machine —
this is not a rare interleaving, it is the normal case.

Three ways to fix it, in order of cost:

```csharp
Interlocked.Increment(ref _counter);   // one atomic CPU instruction. No blocking.

lock (_gate) { _counter++; }           // a monitor. Blocks. Protects several fields at once.

// or: do not share the state at all — a Channel<T>, one consumer, no lock (module 04).
```

**`Interlocked`** covers `Increment`, `Decrement`, `Add`, `Exchange` and `CompareExchange`. That
last one is the primitive every lock-free algorithm is built from:

```csharp
// "set _value to newValue, but only if nobody changed it since I read it"
int original = Interlocked.CompareExchange(ref _value, newValue, expected);
if (original == expected) { /* I won */ }
```

If that shape looks familiar, it should: it is **optimistic concurrency**, identical in structure
to the `rowversion` check in module 07. Read, compute, write conditionally, retry on conflict —
one at the CPU level, one at the database level, the same idea.

**Rules for `lock`, all of which come from real outages:**

- Lock on a **private readonly** field — in .NET 9+, a `System.Threading.Lock`; before that, a
  `private readonly object`.
- **Never** on `this`, on a `Type`, or on a string. Anything a stranger can also lock on is a
  deadlock you cannot see in your own file. (Interned string literals are shared *process-wide*.)
- **Never `await` inside a lock.** The compiler forbids it, because a monitor is owned by a
  *thread* and the continuation may resume on a different one. When you need an async critical
  section, use `SemaphoreSlim.WaitAsync()`.
- **Hold it for as little code as possible, and never call out to unknown code while holding it.**
  An event handler, a virtual method or a callback invoked under your lock can take another lock,
  and now you have a lock-ordering problem you did not write.
- **Take multiple locks in a globally consistent order.** Thread A takes X then Y while thread B
  takes Y then X is *the* classic deadlock, and the fix is a documented ordering, not cleverness.

**The rest of the toolbox, briefly:**

| Primitive | For |
|---|---|
| `Interlocked` | one variable, one operation |
| `lock` / `Monitor` | a short critical section over several fields |
| `SemaphoreSlim` | limiting concurrency to N — and the only one with an async `Wait` |
| `ReaderWriterLockSlim` | many readers, rare writers, **and** a genuinely expensive critical section |
| `ManualResetEventSlim` / `CountdownEvent` | one-off signalling between threads |
| `Lazy<T>` | thread-safe one-time initialisation, done correctly for you |
| `Channel<T>` | not sharing the state in the first place — usually the best answer |

`ReaderWriterLockSlim` deserves a warning: it is several times more expensive to acquire than a
plain `lock`, so for a short read it is *slower*. Use it when reads are long and writes are rare,
and measure it, because the intuition is wrong more often than not.

---

## 3. The memory model — the part nobody teaches you

Here is the code that makes the point:

```csharp
private bool _stop;      // no volatile, no lock
private int _value;

// Thread A
_value = 42;
_stop = true;

// Thread B
while (!_stop) { }       // may loop FOREVER, on a correct runtime, with no bug in sight
Console.WriteLine(_value);
```

Thread B may never see `_stop` become true, and if it does, it might still print `0`. Nothing is
broken. Two mechanisms are at work and both are legal:

1. **The compiler and JIT may reorder** reads and writes that are not observably dependent — and
   may hoist `_stop` out of the loop entirely into a register, because within that thread nothing
   writes to it.
2. **The CPU has per-core caches and store buffers.** A write on core 1 becomes visible to core 2
   *eventually*, and stores can become visible in a different order than they were issued.

Single-threaded code is unaffected: the runtime guarantees that a thread always sees its own
operations in program order. The guarantee simply says nothing about what *another* thread sees.

**The tools that impose order:**

| Tool | Guarantees |
|---|---|
| `lock` | full fence on entry and exit — everything before the release is visible after the acquire |
| `volatile` | acquire semantics on read, release on write. No tearing, no hoisting, no reordering *past* it |
| `Interlocked.*` | atomic **and** a full fence |
| `Volatile.Read/Write` | the same as `volatile`, applied at one call site |

> **The practical rule: if you `lock` around every access, you never need to think about any of
> this.** `volatile` is for the narrow case of a single flag written by one thread and read by
> another, and it is easy to reach for and get wrong.

**What `volatile` does *not* do:** make `_counter++` atomic. It is still read-add-write, and
`volatile` only orders the individual reads and writes. This is the most common misuse of the
keyword in existence.

**Tearing.** Reads and writes up to the platform word size (`int`, `bool`, `float`, any
reference) are atomic — you never see half a value. **`long` and `double` are not atomic on
32-bit**, and *no* struct larger than a word ever is. So a `DateTime` field (two words) or a
`decimal` (four) read while another thread writes it can be an interleaved value that has never
existed. Use `Interlocked.Read`, or a lock, or make it immutable and swap the whole reference —
which is atomic.

**False sharing**, for completeness: two variables that are unrelated but land in the same 64-byte
cache line will bounce that line between cores as if they were shared. It shows up as a parallel
loop that gets *slower* with more threads. The fix is padding, and you will meet this exactly once
in a career, in a counter array.

---

## 4. Parallelism, when the work is actually CPU-bound

```csharp
Parallel.ForEach(orders, order => Recalculate(order));

var totals = orders.AsParallel()
                   .Where(o => o.Total > 1000)
                   .Sum(o => o.Total);
```

`Parallel` and PLINQ are for **CPU-bound** work over a large collection. They partition the work
across pool threads and block until done. That means:

- **Never use them for I/O.** `Parallel.ForEach` with a blocking database call is a starvation
  machine. For concurrent I/O use `await Task.WhenAll(...)`, or `Parallel.ForEachAsync` (.NET 6+),
  which takes a `CancellationToken` and a `MaxDegreeOfParallelism` and awaits properly.
- **The body must be thread-safe.** Writing to a shared `List<T>` from a parallel body is
  undefined and *will* corrupt it. Use `ConcurrentBag`, or the overload with a thread-local
  accumulator, or just project and aggregate afterwards.
- **There is a fixed overhead.** Below a few thousand items of real work, the partitioning costs
  more than it saves. Measure it (module 14) — the naive parallel version is very often slower.
- **Exceptions are aggregated.** `Parallel` collects them into an `AggregateException`, so your
  `catch (SqlException)` will not match. Same for `Task.WaitAll`.

**`Task.WhenAll` is the I/O version, and it has one trap worth knowing:** if several tasks fault,
the exception you get from `await` is only the *first* one. The rest are on the returned task's
`Exception` property, and are otherwise silent.

```csharp
Task all = Task.WhenAll(tasks);
try { await all; }
catch { foreach (var e in all.Exception!.InnerExceptions) Log(e); }
```

---

## 5. Where this meets `async` — the deadlock

The single most expensive bug in .NET, and it is a cycle of two:

```
   1. Get() blocks the context thread on .Result
   2. the await inside captured that context, and posts its continuation to it
   3. the context thread is busy blocking, so the continuation never runs
   4. the task never completes, so the block never ends
```

`dotnet run deadlock` reproduces it against a real single-threaded `SynchronizationContext` — the
same shape a UI or a Blazor Server circuit installs — with a timeout so the demo can still exit.
In production there is no timeout. The thread is simply gone, and it happens again on the next
request until the pool is empty.

**Why it never happens in your console app or your unit test:** ASP.NET Core has had **no**
synchronization context since .NET Core 1.0, and a console app has none either. So the code that
deadlocks in Blazor Server, WPF, WinForms and legacy ASP.NET passes every test you write.
`dotnet run context` prints `SynchronizationContext.Current` as `null` to make that concrete.

**The fixes, in order:**

1. **Do not block.** `async` all the way up to the framework. It is not a local decision.
2. **`ConfigureAwait(false)` on every await in library code** — a library does not know whose
   thread it is on. Application code in ASP.NET Core does not need it; Blazor Server and WPF do.
3. **Never `Task.Run(() => X()).Result` as a workaround.** It does avoid the deadlock, and it
   burns two threads per call to do the work of zero.

And the corollary: **`.Result` and `.Wait()` wrap exceptions in an `AggregateException`**, so
your catch blocks stop matching. `.GetAwaiter().GetResult()` at least rethrows the original — but
you have still blocked a thread, which was the actual problem.

---

## 6. Do this

```bash
cd labs/Labs.Playground
dotnet run race          # then raise perThread to 2,000,000 and watch the loss get worse
dotnet run threadpool
dotnet run context
dotnet run deadlock
```

Then break it deliberately:

1. In `Demos.Concurrency.cs`, replace `Interlocked.Increment(ref _interlockedCounter)` with
   `_interlockedCounter++` and confirm the second number goes wrong too. Then put it back.
2. In `RaceCondition`, change the `lock` body to increment **two** fields and assert they stay
   equal — this is the case `Interlocked` cannot express, and the reason `lock` exists.
3. In `Demos.Concurrency.cs`, add `.ConfigureAwait(false)` to the `await Task.Delay(10)` inside
   the deadlock demo's `GetAsync`, re-run, and watch the deadlock disappear.

---

## 7. Golden rules

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

---

## 8. Interview questions

**"Difference between a thread, a task, and a core?"**
A core is real parallelism and you have a fixed number. A thread is an OS scheduling unit costing
about a megabyte of stack and a millisecond to create. A task is a promise of a future result — it
may run on a pool thread, or on no thread at all if it is I/O.

**"What is thread-pool starvation and how do you spot it?"**
Work items are blocking pool threads faster than the pool injects new ones, which it does at
roughly one per second above the core count. You spot it by latency rising while CPU stays low,
and by a climbing `ThreadPool.ThreadCount`. The cause is always blocking on async work.

**"Is `counter++` thread-safe? What about with `volatile`?"**
No, and no. It is read-add-write; two threads can interleave and lose an update. `volatile` orders
the individual reads and writes but does nothing about the gap between them. Use
`Interlocked.Increment`, or a lock.

**"What does `volatile` actually guarantee?"**
Acquire semantics on read and release on write: the value is not cached in a register, and reads
and writes are not reordered past it. It prevents the "loop forever on a stale flag" bug. It does
not make compound operations atomic.

**"Why should you not lock on `this` or on a string?"**
Because any other code holding the same reference can lock on it too. String literals are interned
process-wide, so two unrelated libraries locking on `"lock"` share one lock. You cannot reason
about a lock whose other users you cannot enumerate — so use a private readonly field.

**"Why can you not `await` inside a `lock`?"**
A monitor is owned by the thread that entered it, and the continuation after an `await` may resume
on a different thread — which would then try to release a lock it does not own. The compiler
rejects it. `SemaphoreSlim.WaitAsync` is the async-friendly alternative.

**"Explain the classic async deadlock."**
A synchronous caller blocks on `.Result`. The `await` inside captured a single-threaded
synchronization context and posts its continuation there. That thread is blocked, so the
continuation never runs and the task never completes. It cannot happen in ASP.NET Core, which has
no context, which is why it survives testing and appears in Blazor Server or WPF.

**"When would you use `Parallel.ForEach` instead of `Task.WhenAll`?"**
`Parallel.ForEach` for CPU-bound work over a collection — it partitions across pool threads and
blocks. `Task.WhenAll` for I/O, where no thread should be held at all. Using `Parallel.ForEach`
around blocking I/O is a reliable way to starve the pool.

---

---

## Do the lab

```bash
dotnet test labs/Labs.Exercises --filter "FullyQualifiedName~Lab06"
```

Exercises 3 and 4 of [`Lab06_Disposal.cs`](../../labs/Labs.Exercises/Exercises/Lab06_Disposal.cs) are
this module: a counter that survives 200 000 concurrent increments, and an async cache where fifty
simultaneous misses cause one load. Write exercise 3 wrong first — the failure prints a different
number every run, which is the whole lesson. Answers in [SOLUTIONS.md](../SOLUTIONS.md).

## Next

→ [Module 22 — Inside the CLR](../module-22-clr-internals/)
