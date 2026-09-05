# Module 04 — Async and concurrency

> The most misunderstood feature in .NET. Get this wrong and you get deadlocks, thread-pool
> starvation, and a server that falls over at a load a synchronous version would have survived.

---

## Deeper chapters

| | Chapter | |
|---|---|---|
| 3 | [`ConfigureAwait`, and why CA2007 is off here](04-configureawait.md) | on in libraries, off in applications — and why that is not laziness |

---

## 1. `async` does not mean "on another thread"

```bash
cd labs/Labs.Playground && dotnet run async
```

Watch the output. `DemoAsync()` runs **synchronously** until the first `await`. At the `await` it
returns an incomplete `Task` to the caller, and the rest of the method is scheduled as a
continuation — possibly on a different thread, possibly the same one, possibly much later.

**No thread was blocked waiting.** That is the whole point.

> **Five more demos build directly on this one.** `dotnet run context` installs a UI-like
> synchronization context so you can watch `ConfigureAwait(false)` change which thread resumes;
> `dotnet run deadlock` reproduces the classic sync-over-async deadlock safely;
> `dotnet run threadpool` shows starvation; `dotnet run race` shows what happens when two threads
> genuinely run at once; `dotnet run valuetask` covers the consume-once rule. The theory behind
> all five is [module 21](../module-21-threading-and-memory-model/).

```
Synchronous I/O:   [thread] ──── blocked 200ms waiting for the database ──── [thread]
Asynchronous I/O:  [thread] ──► returns to pool ... I/O completes ... [any thread] resumes
```

A server handling 10,000 idle connections needs roughly **zero** threads, not 10,000. Async frees
threads; it does not create them.

**Corollary:** `async` over CPU-bound work buys nothing — there is no I/O to wait on. That is
what `Task.Run` is for, and it should be rare in server code.

```
   I/O bound — database, HTTP, disk, queue   ──►  await it. No extra thread. This is the point.
   CPU bound and must not block the caller   ──►  Task.Run — and it should be rare on a server
   "fire and forget"                         ──►  it is not. Give it a token and observe it,
                                                  or do not start it
   producer / consumer                       ──►  Channel<T>, BOUNDED — unbounded is a leak
```

---

## 2. The state machine

The compiler rewrites an `async` method into a struct implementing `IAsyncStateMachine`, with
each `await` as a resume point — the same transformation as `yield return` (module 03).

```
   async Task<T> HandleAsync()             the state machine the compiler emits
   {                                       ┌────────────────────────────────────────┐
       var a = Prepare();                  │ state -1  runs on the CALLER's thread, │
                                           │           synchronously, up to here    │
       var b = await QueryAsync();  ──────►│ state  0  ◄─ resume point              │
                                           │           thread goes back to the pool │
       return Combine(a, b);        ──────►│ state  1  ◄─ resume point              │
   }                                       │           ANY pool thread resumes here │
                                           └────────────────────────────────────────┘

   the method returns an incomplete Task at the first await.
   nothing was blocked. nothing was moved to another thread.
```

Practical consequences:

- **An `async` method with no `await` runs entirely synchronously** and warns (CS1998). If you
  just need a completed task, return `Task.FromResult(x)` — no state machine, no allocation.
- **`ValueTask<T>` exists** for methods that usually complete synchronously (a cache hit). It
  avoids the `Task` allocation. Use it only in genuinely hot paths — it cannot be awaited twice,
  which makes it easier to misuse.

---

## 3. The rules that prevent the classic failures

### Never block on async code

```csharp
var result = SomeAsyncMethod().Result;      // ❌
SomeAsyncMethod().Wait();                   // ❌
var x = SomeAsyncMethod().GetAwaiter().GetResult();  // ❌
```

In any context with a synchronisation context (classic ASP.NET, WinForms, WPF) this **deadlocks**:
the continuation needs the context, the context is held by the blocked thread, neither proceeds.

ASP.NET Core has no synchronisation context, so it does not deadlock — it just starves the thread
pool instead, which fails under load rather than immediately. Arguably worse, because you ship it.

**`async` all the way down.** If a method calls async code, it is async.

Analyser `CA1849` catches this and is enabled in [`.editorconfig`](../../.editorconfig).

### `async void` — only for event handlers

```csharp
async void Handler(object s, EventArgs e)   // exception here CRASHES THE PROCESS
```

An `async void` method has no `Task` to carry an exception, so it goes to the thread pool's
unhandled-exception handler and terminates the process. Use `async Task` everywhere except a
literal event handler.

### Pass the `CancellationToken`

Every async method in this codebase takes one and passes it on. Without it, a user who closes
their browser leaves the query running, holding a connection and a thread until it completes.

```csharp
public Task<Order?> GetAsync(OrderId id, CancellationToken cancellationToken = default) =>
    context.Orders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
```

📂 [`GlobalExceptionHandler.cs`](../../src/LogiFlow.Api/Infrastructure/GlobalExceptionHandler.cs) maps
`OperationCanceledException` to **499**, not 500 — the client went away, you did not fail. Logging
those as 5xx pollutes your error budget with events you did not cause.

### `ConfigureAwait(false)` in libraries

`await foo.ConfigureAwait(false)` means "resume anywhere, I do not need the original context".

- **In a library** (Application, Infrastructure): always. It avoids capturing a context you do
  not need, and prevents the deadlock above when a caller does block.
- **In ASP.NET Core application code**: unnecessary — there is no synchronisation context to
  capture.

This codebase uses it in the layers that behave as libraries. `CA2007` is turned off in
`.editorconfig` precisely so it is not demanded in the API project too.

---

## 4. Parallelism

`await` is sequential. To run things concurrently, start them and then await:

```csharp
// Sequential: 300ms
var a = await GetOrdersAsync();
var b = await GetProductsAsync();

// Concurrent: ~150ms
Task<X> ta = GetOrdersAsync();
Task<Y> tb = GetProductsAsync();
await Task.WhenAll(ta, tb);
```

📂 [`ValidationBehavior.cs`](../../src/LogiFlow.Application/Behaviors/ValidationBehavior.cs) runs every
validator concurrently with `Task.WhenAll`.

**The trap: `DbContext` is not thread-safe.** Firing two EF queries concurrently on one context
throws *"A second operation was started on this context instance"*. To parallelise database work
you need a context per operation, via `IDbContextFactory`.

**`Task.WhenAll` and exceptions:** it throws only the *first* exception. To see them all, inspect
`task.Exception` (an `AggregateException`) after awaiting.

---

## 5. `IAsyncEnumerable<T>`

Stream results instead of materialising them:

```csharp
await foreach (Order order in context.Orders.AsAsyncEnumerable().WithCancellation(ct))
{
    // processed one at a time; the whole table is never in memory
}
```

Right for exports and batch jobs over large tables. Wrong for a web response, where you want a
bounded page.

---

## 6. `Channel<T>` — producer/consumer done properly

```csharp
Channel<Order> channel = Channel.CreateBounded<Order>(100);
await channel.Writer.WriteAsync(order, ct);        // blocks when full — backpressure
await foreach (var o in channel.Reader.ReadAllAsync(ct)) { }
```

**Bounded, not unbounded.** An unbounded channel with a producer faster than its consumer is a
memory leak with extra steps. A bounded channel applies backpressure, which is what you want.

---

## 7. `BackgroundService`

📂 [`Outbox/OutboxProcessor.cs`](../../src/LogiFlow.Infrastructure/Persistence/Outbox/OutboxProcessor.cs)

Three things that catch people out, all handled there:

1. **It is a singleton, so it cannot inject scoped services.** No `DbContext` in the constructor —
   use `IServiceScopeFactory` and create a scope per iteration. Reusing one context for days also
   accumulates tracked entities and leaks memory.
2. **An unhandled exception kills the host** (default since .NET 6). That is the right default —
   a silently dead background worker is worse than a crash — but it means the loop body must
   catch everything itself.
3. **Honour `stoppingToken`** or the host hangs for its full shutdown timeout on every deploy,
   and orchestrators eventually `SIGKILL` you mid-write.

Note it uses `PeriodicTimer` rather than `Task.Delay` in a loop: no drift, no allocation per tick,
clean cancellation.

---

## 8. Golden rules

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

---

## 9. Interview questions

**"Does `async` create a thread?"**
No. It frees the current thread while I/O is in flight. The continuation runs on a thread-pool
thread when the I/O completes. `Task.Run` is what moves work to another thread, and it is for
CPU-bound work, not I/O.

**"What does `await` actually do?"**
The compiler rewrites the method into a state machine. At an `await`, if the awaited task is not
complete, the method registers a continuation and returns; when the task completes, execution
resumes at that point.

**"Why is `.Result` dangerous?"**
It blocks. With a synchronisation context it deadlocks — the continuation needs the context, the
blocked thread holds it. Without one it starves the thread pool, which fails under load rather
than immediately.

**"What is `ConfigureAwait(false)` for?"**
It tells the runtime not to marshal the continuation back to the captured context. Use it in
libraries; unnecessary in ASP.NET Core application code, which has no synchronisation context.

**"`async void`?"**
Only for event handlers. Exceptions cannot be observed by the caller and crash the process.

**"How do you run two async operations concurrently?"**
Start both (do not await immediately), then `await Task.WhenAll`. Note that this is unsafe with a
shared `DbContext`, which is not thread-safe — use a context factory.

---

## Next

→ [Module 05 — Clean Architecture and DDD](../module-05-clean-architecture/)
