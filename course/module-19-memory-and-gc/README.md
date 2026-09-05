# Module 19 — Memory, the heap, and the garbage collector

> You have written C# for years without thinking about memory. That is the point of a managed
> runtime, and it works right up until the day a service grows to 4 GB overnight and nobody can
> say why. This module is the mental model that makes that day short.

Everything here is observable. Run it as you read:

```bash
cd labs/Labs.Playground
dotnet run gc          # generations, promotion, the LOH line
dotnet run finalizers  # why a finalizer is not a destructor
dotnet run disposal    # using, order, and IAsyncDisposable
dotnet run pooling     # ArrayPool, and its two traps
dotnet run leaks       # a leak in a garbage-collected language
```

---

## 1. Stack, heap, and what a variable actually holds

Two questions decide where a thing lives, and they are not the same question:

```
   WHAT IS IT?                              WHERE DOES IT LIVE?
   value type  (int, struct, enum)          wherever its container lives
   reference type (class, string, array)    the heap, always
```

That second column is the part people get wrong. A value type does **not** always live on the
stack — it lives *inside whatever holds it*. An `int` local lives on the stack. An `int` field of
a class lives on the heap, inside that object. An `int` captured by a lambda lives on the heap,
inside the compiler-generated closure class. A `struct` in a `List<T>` lives on the heap, inline
in the backing array.

```
       STACK  (per thread, ~1 MB, freed by decrementing a pointer)
       ┌──────────────────────┐
       │ int quantity  = 5    │   the value itself
       │ Order order   = ───────────┐
       │ OrderId id    = [Guid]│    │   a struct local: 16 bytes, right here
       └──────────────────────┘    │
                                   ▼
       HEAP  (shared, freed by the GC)
       ┌───────────────────────────────────┐
       │ Order                             │
       │   header  (8 bytes: sync block)   │  ← lock, hash code
       │   method table pointer (8 bytes)  │  ← "what type am I"
       │   OrderId Id      [Guid, inline]  │  ← a struct FIELD, on the heap
       │   Customer? Buyer ──────────────────────► another object
       └───────────────────────────────────┘
```

**The 16 bytes of overhead** on that diagram are why `new object()` costs 24 bytes on 64-bit, and
why a `List<Point>` of a million small structs is dramatically smaller than a `List<PointClass>`
of the same data: one array versus a million objects, each with a header.

**The stack is not free of consequence either.** It is about 1 MB per thread by default. Deep
recursion overflows it, and a `StackOverflowException` cannot be caught — the process dies
immediately, by design, because the runtime has nowhere left to run a handler.

---

## 2. Allocation is a pointer bump

The single most surprising fact about .NET performance: **allocating is nearly free.**

```
   before:   [ obj ][ obj ][ obj ]▏                    ← the allocation pointer
   new():    [ obj ][ obj ][ obj ][ new ]▏             ← moved. That is the whole operation.
```

No free-list search, no fragmentation scan. A malloc in C has to *find* space; the .NET
allocator only has to *advance*. What costs is not the allocation — it is the **collection** that
eventually has to happen, and specifically it is proportional to the number of objects that
**survive**.

That single sentence reverses most naive performance advice:

> **The GC does not charge you for garbage. It charges you for survivors.**

A million objects allocated and dropped inside one request cost almost nothing. Ten thousand
objects that live for an hour cost you on every full collection for that hour.

---

## 3. Generations

Empirically, most objects die young — a DTO, a string, a closure, a `Task`. The GC is built
entirely around exploiting that.

```
    gen 0 ───────► gen 1 ───────► gen 2                            LOH (≥ 85,000 bytes)
    the nursery    a buffer       long-lived                       allocated straight into
    a few MB       survived 1     survived 2+                      gen 2, never compacted
                                                                   (by default)
    collected      collected      collected rarely — and this
    constantly     sometimes      one is the expensive one
    < 1 ms                        (10s of ms; blocking, if it has to be)
```

A collection of generation *N* also collects every generation below it. So a gen-2 collection is
a **full** collection: mark everything reachable, sweep, and usually compact. Gen 0 only has to
walk the nursery.

**How gen 0 stays cheap when older objects point into it** is worth knowing, because it is a
favourite interview question. If a gen-2 object holds a reference to a gen-0 object, the
collector must not free that gen-0 object — but it also must not scan all of gen 2 to find out.
The answer is the **write barrier**: every reference-typed field assignment goes through a tiny
piece of runtime code that records the memory region in a **card table**. A gen-0 collection
scans only the dirtied cards. This is also why assigning a reference field costs marginally more
than assigning an `int`, and why an array of structs beats an array of classes twice over.

**The Large Object Heap.** Anything ≥ 85,000 bytes skips the nursery entirely and is born in gen
2. It is not compacted by default, so it fragments like a C heap: you can have 500 MB free and
still fail to allocate a 10 MB array. `GCSettings.LargeObjectHeapCompactionMode` exists, and
using it routinely is an admission you should have pooled the buffer instead.

**Server GC vs Workstation GC** is the one configuration switch that matters:

| | Workstation (default for a console app) | Server (default for ASP.NET Core) |
|---|---|---|
| Heaps | one | one per core |
| Collector threads | one, background | one per heap |
| Optimises for | latency on one core | throughput across many |
| Memory used | less | more — deliberately |

```xml
<ServerGarbageCollection>true</ServerGarbageCollection>
<ConcurrentGarbageCollection>true</ConcurrentGarbageCollection>
```

In a container, set `<ServerGarbageCollection>` deliberately and give the container a memory
limit the runtime can see. Server GC on a 64-core host inside a 512 MB container is a well-known
way to get an out-of-memory kill; .NET reads cgroup limits, but only if the limit is actually
set.

---

## 4. What "reachable" means, and therefore what a leak is

The collector starts from a set of **roots** and marks everything it can reach:

```
   ROOTS
   ├── every local and argument on every thread's stack
   ├── every static field in every loaded assembly   ← the one that bites
   ├── CPU registers
   ├── GC handles (pinned objects, GCHandle, interop)
   └── the finalization queue
```

Anything not reachable from a root is garbage. Which gives the precise definition:

> **A leak in .NET is not memory you forgot to free. It is a reference you forgot you were
> holding.**

The five ways it happens, in the order you will meet them:

1. **An event handler never unsubscribed.** The publisher holds the delegate; the delegate holds
   the subscriber. The arrow points *from* the long-lived thing *to* the short-lived one, which
   is backwards from how you drew it in your head. `dotnet run leaks` shows it, and shows `-=`
   fixing it.
2. **A static collection.** A static field is a root by definition, so a `static
   List<Order>` that is only ever added to is not a cache — it is a leak with a nice name.
3. **A cache with no eviction.** `MemoryCache` without `SizeLimit` is an unbounded dictionary
   with good manners. Set a size, set an expiry, and know which one is doing the work.
4. **A captured closure held by something long-lived.** A lambda that touches any field captures
   `this` — the *whole object*, not the field. Hand that lambda to a timer or a long-running
   `Task` and the object graph is pinned to it.
5. **An undisposed `IDisposable` holding native memory.** The managed wrapper is 32 bytes, so
   the GC feels no pressure to collect it, while the 50 MB native buffer behind it stays alive.
   This is what `AddMemoryPressure` exists for, and what `using` exists to make unnecessary.

**How you actually find one, in order:**

```
   1. Is memory rising, or just high?      A high steady heap is a working set, not a leak.
   2. Does it survive a full GC?           dotnet-counters, or GC.GetTotalMemory(true).
   3. Which type is growing?               Two dumps ten minutes apart; diff the counts.
   4. Who is holding it?                   dotnet-gcdump, then look at the ROOT PATH.
```

```bash
dotnet-counters monitor --process-id <pid> System.Runtime
dotnet-gcdump collect -p <pid>          # take two, ten minutes apart, and diff
dotnet-dump collect -p <pid>            # then: dumpheap -stat / gcroot <address>
```

The type whose instance count only ever rises is your answer, and `gcroot` tells you the chain
of references keeping it alive. That chain is the bug, and it is usually one line long.

---

## 5. `IDisposable`, and the two-line rule most code gets wrong

The GC manages **memory**. It does not manage file handles, sockets, database connections, locks
or native allocations. Those need releasing at a *known moment*, which is what `IDisposable` is.

```csharp
public void Dispose()
{
    if (_disposed) return;      // idempotent: Dispose may legally be called twice
    _disposed = true;
    _connection.Dispose();
    GC.SuppressFinalize(this);  // only if you actually have a finalizer
}
```

**The rules, and they are short:**

- **If you own something disposable, you are disposable.** Ownership is transitive.
- **If you did not create it, do not dispose it.** Disposing an injected `HttpClient` or a
  `DbContext` the container owns is how you get *"cannot access a disposed object"* two requests
  later.
- **`Dispose` must be idempotent and must not throw.** It runs on the exception path, where
  throwing replaces the real error with yours.
- **Never write a finalizer unless you hold unmanaged memory directly.** Which, in application
  code, you never do — `SafeHandle` already did it for you.

`dotnet run finalizers` shows exactly why that last rule matters: a finalizer takes a collection,
a queue, a second thread and an explicit wait to run, it forces the object to survive an extra
generation, and at process exit it may simply never run at all.

**`IAsyncDisposable`** exists because `Dispose()` cannot `await`. A synchronous flush over a
network has two bad options — block a thread or drop the data — so `await using` is the third.
The trap: a type implementing **both** silently gets the synchronous one from a plain `using`.
Write `await using` whenever the type offers it.

---

## 6. Reducing allocation, in the order that pays

Only after you have measured (module 14). In descending order of return:

1. **Do not allocate in a loop that runs per row.** The biggest wins are structural — one query
   instead of N, one buffer instead of one per item.
2. **`Span<T>` / `ReadOnlySpan<T>` for slicing.** Parsing, searching and trimming without
   copying. `dotnet run spans`.
3. **`ArrayPool<T>.Shared` for buffers over a few KB**, especially any that would cross the
   85,000-byte LOH line. `dotnet run pooling` — and note both traps: `Rent` returns a buffer
   *at least* that big (never use `.Length`), and a rented buffer is **dirty** (clear it if it
   held anything sensitive).
4. **`StringBuilder`, or better, `string.Create` / interpolated string handlers.** `dotnet run
   strings` shows the O(n²) that `+=` in a loop really is.
5. **`struct` for small, short-lived values** — and `readonly record struct` so the compiler
   stops defensive copies. Module 01 has the ≤16-byte guidance.
6. **`stackalloc` for a small, known-size scratch buffer** — but only under a size guard, because
   a `stackalloc` inside a loop is how you overflow a stack you cannot catch.

And one thing *not* to do: **never call `GC.Collect()` in production code.** It forces a full
blocking collection, throws away everything the collector had learned about your allocation
rates, and promotes every survivor. The only legitimate uses are a benchmark harness and a
teaching demo — which is precisely what `dotnet run gc` is.

---

## 7. Do this

```bash
cd labs/Labs.Playground
dotnet run gc              # find your LOH line; watch a live object get promoted 0 → 1 → 2
dotnet run leaks           # then read the code and predict which WeakReference stays alive
dotnet run pooling         # note the dirty buffer. Imagine it held a token.
dotnet run finalizers
```

Then break something on purpose, which is where the understanding actually lands:

1. In `Demos.Memory.cs`, change `new byte[100_000]` to `new byte[80_000]` and re-run `gc`.
   Watch the generation change from 2 to 0. You have just found the LOH line by bisection.
2. In the `leaks` demo, comment out the `Unsubscribe` call and re-run. Both `WeakReference`s
   now report `alive = True`.
3. Add `<ServerGarbageCollection>true</ServerGarbageCollection>` to
   `labs/Labs.Playground/Labs.Playground.csproj`, re-run `gc`, and watch the heap counts and
   the `Server GC` line change.

---

## 8. Golden rules

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

---

## 9. Interview questions

**"Where do value types live?"**
Wherever their container lives. A local `int` is on the stack; an `int` field of a class is on
the heap inside that object; a captured local is on the heap inside the closure object. The
common answer "structs are on the stack" is close enough to be wrong in exactly the cases that
matter.

**"Why is the GC generational?"**
Because most objects die young. Collecting only the nursery gets nearly all the garbage for a
fraction of the work. Gen 2 is collected rarely because it is the expensive one, and a gen-N
collection implies every generation below it.

**"If gen 0 is collected on its own, how does it know an old object references a young one?"**
The write barrier. Every reference-field assignment marks the containing memory region in the
card table, and a gen-0 collection scans only the dirtied cards rather than the whole old heap.

**"What is the Large Object Heap and why do I care?"**
Objects ≥ 85,000 bytes. They are allocated directly into gen 2 and are not compacted by default,
so the LOH fragments — you can hold plenty of free memory and still fail a large allocation.
Anything that big and repetitive should come from an `ArrayPool`.

**"Can you leak memory in C#?"**
Yes, easily. The GC frees unreachable objects, so anything still referenced is by definition not
a leak to the runtime. Events, statics, unbounded caches and captured closures held by
long-lived objects are the four causes. You find it by diffing two gcdumps and following the
root path.

**"`Dispose` vs a finalizer?"**
`Dispose` is deterministic, called by you or by `using`, and is where all cleanup belongs. A
finalizer is non-deterministic, runs on the finalizer thread, costs the object an extra
generation of lifetime, and exists solely as a last-resort net for unmanaged memory. If you have
one, `Dispose` should call `GC.SuppressFinalize(this)`.

**"How would you diagnose a service whose memory grows all day?"**
Confirm it survives a forced full collection, so it is a leak and not a working set. Then
`dotnet-counters` for the trend, two `dotnet-gcdump`s ten minutes apart to find the type whose
count only rises, then `gcroot` on an instance to find who is holding it. The answer is almost
always one reference.

---

---

## Do the lab

```bash
dotnet test labs/Labs.Exercises --filter "FullyQualifiedName~Lab06"
```

[`Lab06_Disposal.cs`](../../labs/Labs.Exercises/Exercises/Lab06_Disposal.cs) covers disposal that is
safe to call twice, `IAsyncDisposable`, and — with module 21 — the shared-state exercises. Answers in
[SOLUTIONS.md](../SOLUTIONS.md).

## Next

→ [Module 20 — Equality, hashing and collections](../module-20-equality-and-collections/)
