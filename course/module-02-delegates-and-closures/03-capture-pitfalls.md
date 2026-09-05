# 3. The closure trap

> Part of [Module 02 — Delegates, lambdas and closures](README.md), section 3.
> Previous: [2. A lambda is a class](README.md#2-a-lambda-is-a-class) ·
> Next: [4. `Func<T>` vs `Expression<Func<T>>`](../module-03-linq-internals/03-expression-trees.md)

---

A lambda that uses a variable from its enclosing scope does not copy the value. It **captures the
variable** — and keeps it alive, shared, for as long as the lambda exists.

Almost every closure bug follows from that one sentence.

## The one everybody meets

```csharp
var actions = new List<Action>();

for (int i = 0; i < 3; i++)
    actions.Add(() => Console.Write(i));

foreach (Action a in actions) a();     // 333 — not 012
```

There is one `i` for the whole loop. All three lambdas captured *that variable*, and by the time
anybody invokes them the loop has finished and `i` is 3.

```csharp
for (int i = 0; i < 3; i++)
{
    int captured = i;                  // a fresh variable per iteration
    actions.Add(() => Console.Write(captured));
}
// 012
```

One extra line. `captured` is declared inside the loop body, so each iteration creates a new one and
each lambda closes over a different variable.

### Why `foreach` is different

```csharp
foreach (int n in new[] { 0, 1, 2 })
    actions.Add(() => Console.Write(n));    // 012, since C# 5
```

C# 5 changed `foreach` so the iteration variable is a **fresh variable per iteration**. That was a
deliberate breaking change, made because the old behaviour was almost never what anybody wanted.

`for` was left alone, and correctly so: its variable is one *you* declared, initialised and mutate.
"Resetting" it each iteration would break `for (int i = 0; i < n; i += 2)` and every other loop whose
variable carries state between iterations. So the inconsistency is real, deliberate, and the right
call — which is exactly what an interviewer wants you to be able to explain.

## What the compiler actually writes

```csharp
// You write:
int factor = 3;
Func<int, int> multiply = x => x * factor;

// Roughly what the compiler emits:
private sealed class <>c__DisplayClass0_0
{
    public int factor;                                  // the variable, hoisted to a FIELD
    internal int <Method>b__0(int x) => x * this.factor;
}
```

The captured variable is **moved onto a heap-allocated object**. `factor` is no longer a local at
all — the method body and the lambda both read the same field.

Three consequences:

**Mutation is visible in both directions.**

```csharp
int factor = 3;
Func<int, int> f = x => x * factor;
factor = 10;
f(2);          // 20, not 6
```

Sometimes exactly what you want, usually a surprise.

**A capture is an allocation.** The display class is a heap object created every time the enclosing
method runs. In a hot path — a loop, a request handler — capturing turns a free lambda into an
allocation per call. Which is why `Dispatcher` uses the state-passing overload:

```csharp
// ✗ captures `responseType` → a display class per call
RequestHandlerCache.GetOrAdd(requestType, type => Build(type, responseType));

// ✓ static lambda, state passed as an argument → no capture, no allocation
RequestHandlerCache.GetOrAdd(requestType, static (type, state) => Build(type, state), responseType);
```

The `static` keyword on a lambda (C# 9) makes capturing a **compile error**, which is how you enforce
this rather than hope for it.

**Captures extend lifetime.** Anything captured lives as long as the delegate. Capture a big object
in a lambda stored on a long-lived service and it can never be collected — the same mechanism as the
[event-handler leak](../module-19-memory-and-gc/), arriving from a different direction.

## `this` is captured whole

```csharp
public sealed class ReportGenerator
{
    private readonly byte[] _hugeBuffer = new byte[50_000_000];

    public Func<int> GetCount() => () => _count;   // captures `this`, not `_count`
}
```

Referencing any instance member captures the **entire instance**, so the returned lambda keeps 50 MB
alive. The fix is to copy what you need into a local first:

```csharp
public Func<int> GetCount()
{
    int count = _count;
    return () => count;      // captures an int
}
```

This is a common cause of "why is this object still in memory" in a dump.

## `async` and captured loop variables

```csharp
// ✗ Every task may see the same `id`, and they share one DbContext.
foreach (int id in ids)
    tasks.Add(Task.Run(async () => await Process(id)));
```

Two bugs stacked. `foreach` fixes the capture since C# 5, so `id` is fine — but if that were a `for`
loop it would not be. And `Task.Run` on a shared scoped `DbContext` is the
[thread-safety failure](../module-06-efcore/01-dbcontext-and-change-tracking.md) from module 06.

The safe shape is a scope per task:

```csharp
await Parallel.ForEachAsync(ids, ct, async (id, token) =>
{
    await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<LogiFlowDbContext>();
    await Process(db, id, token);
});
```

## The mistakes

**A `for`-loop variable captured in a lambda.** The `333` bug.

**Assuming a lambda snapshots values.** It captures variables, and they keep changing.

**Capturing in a hot path without noticing.** One display class allocation per call. Use `static`
lambdas and the state overloads.

**Capturing `this` accidentally.** Keeps the whole object alive; copy the field into a local.

**Capturing a `ref`, `out` or `in` parameter, or a `ref struct`.** It does not compile — because the
capture would have to outlive the stack frame. `Span<T>` cannot cross into a lambda for the same
reason.

## Try it

```bash
dotnet run --project labs/Labs.Playground closures
dotnet test labs/Labs.Exercises --filter "FullyQualifiedName~Lab05"
```

The demo prints `333` and then `012` from the two versions side by side. Lab 05 exercise 3 makes you
fix it, and exercise 2 makes you *rely* on capture deliberately — a memoising closure whose dictionary
is a captured local, which is the same mechanism working for you instead of against you.

Then decompile with `ildasm` or sharplab.io and look for `<>c__DisplayClass`. Seeing the generated
class once makes the whole topic permanent.

## What to remember

- A closure captures the **variable**, not its value.
- A `for` loop has one variable for the whole loop. Copy it inside the body.
- `foreach` gives a fresh variable per iteration, since C# 5. `for` does not, deliberately.
- The compiler hoists captured variables into a heap-allocated display class.
- Mutation is visible both ways, and capture is an allocation.
- Referencing any instance member captures `this` — the whole object.
- `static` lambdas make accidental capture a compile error. Use them in hot paths.
- `ref`, `out`, `in` and `ref struct` cannot be captured at all.

**Code:** [`Dispatcher.cs`](../../src/LogiFlow.Application/Abstractions/Messaging/Dispatcher.cs) ·
[`Lab05_Delegates.cs`](../../labs/Labs.Exercises/Exercises/Lab05_Delegates.cs)

**Next:** [Expression trees](../module-03-linq-internals/03-expression-trees.md)
