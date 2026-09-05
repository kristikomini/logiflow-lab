# Module 02 — Delegates, lambdas and closures

> Short module, high payoff. The closure trap here is one of the most common bugs in C#, and
> the `Func` vs `Expression` distinction is the foundation for module 03.

---

## Deeper chapters

| | Chapter | |
|---|---|---|
| 3 | [The closure trap](03-capture-pitfalls.md) | capture semantics, the display class, and why `for` differs from `foreach` |

---

## 1. A delegate is a typed method pointer

```csharp
Func<int, int, int> add = (a, b) => a + b;      // takes args, returns a value
Action<string> log = message => Console.WriteLine(message);   // returns void
Predicate<Order> isOpen = o => o.Status == Submitted;         // returns bool
```

`Func`, `Action` and `Predicate` are just pre-declared generic delegate types. You rarely declare
your own — but this codebase does, once, where the name carries meaning:

```csharp
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();
```

📂 [`Abstractions/Messaging/Messages.cs`](../../src/LogiFlow.Application/Abstractions/Messaging/Messages.cs)
— `next()` in a pipeline behaviour reads far better than `Func<Task<TResponse>>`.

---

## 2. A lambda is a class

```csharp
int threshold = 100;
Func<Order, bool> expensive = o => o.Total > threshold;
```

The compiler generates a **display class** holding `threshold` as a field, and a method using it.
`expensive` points at an instance of that class.

```
   int threshold = 100;
   Func<Order,bool> expensive = o => o.Total > threshold;

        │  the compiler emits, roughly:
        ▼
   sealed class <>c__DisplayClass
   {
       public int threshold;                                   ← the LOCAL MOVED here
       public bool <M>b__0(Order o) => o.Total > threshold;
   }

   var display = new <>c__DisplayClass { threshold = 100 };    ← one allocation
   expensive = display.<M>b__0;                                ← delegate over that instance
```

Two consequences:

1. **Capturing allocates.** A lambda that captures nothing is cached and allocates once ever; one
   that captures a variable allocates a display class each time it is created. On a hot path that
   matters — hence `static` lambdas (C# 9), which make capture a compile error:
   ```csharp
   RequestHandlerCache.GetOrAdd(requestType, static (type, responseType) => { ... }, typeof(TResponse));
   ```
   📂 [`Dispatcher.cs`](../../src/LogiFlow.Application/Abstractions/Messaging/Dispatcher.cs) — the state is
   passed as an argument specifically so the lambda can stay `static`.

2. **It captures the variable, not the value.** Which leads to…

---

## 3. The closure trap

```bash
cd labs/Labs.Playground && dotnet run closures
```

```csharp
var actions = new List<Func<int>>();
for (int i = 0; i < 3; i++)
    actions.Add(() => i);

// prints [3, 3, 3] — every lambda closed over the SAME i, which is now 3
```

```
   for (int i = 0; i < 3; i++)          ONE variable, three delegates pointing at it
        ┌── i ───────────────────────────────┐
        │  0 → 1 → 2 → 3   (loop has ended)  │
        └───▲──────▲──────▲──────────────────┘
            │      │      │
           λ0     λ1     λ2      every one of them reads 3

   foreach (var x in xs)               A FRESH variable per iteration — since C# 5
        ┌ x₀ ┐  ┌ x₁ ┐  ┌ x₂ ┐
        └─▲──┘  └─▲──┘  └─▲──┘
         λ0      λ1      λ2          each reads its own
```

The fix is a variable scoped *inside* the loop:

```csharp
for (int i = 0; i < 3; i++)
{
    int copy = i;
    actions.Add(() => copy);   // [0, 1, 2]
}
```

**`foreach` was changed in C# 5** to declare a fresh variable per iteration, so it is safe.
**`for` was not.** That inconsistency is exactly why the bug still catches people.

### It is in this codebase, handled deliberately

📂 [`Dispatcher.cs`](../../src/LogiFlow.Application/Abstractions/Messaging/Dispatcher.cs), building the
behaviour chain:

```csharp
for (int i = behaviors.Length - 1; i >= 0; i--)
{
    IPipelineBehavior<TRequest, TResponse> behavior = behaviors[i];   // copy
    RequestHandlerDelegate<TResponse> next = pipeline;                // copy
    pipeline = () => behavior.HandleAsync(typedRequest, next, cancellationToken);
}
```

Both locals are copies. Capture `i` directly and every closure sees `i == -1` by the time it
runs — the pipeline would throw `IndexOutOfRangeException` on the first request.

**Where this shows up in real code:** registering event handlers in a loop, building a list of
tasks in a loop, or wiring up UI callbacks. Almost always in a `for`, almost always with an index.

---

## 4. `Func<T>` vs `Expression<Func<T>>`

The distinction the rest of the course rests on.

```csharp
Func<Order, bool>             compiled = o => o.Total > 100;   // executable, opaque
Expression<Func<Order, bool>> tree     = o => o.Total > 100;   // inspectable data
```

Identical syntax; the compiler picks based on the target type. The second builds an object graph
you can walk, transform, and translate to another language.

```csharp
tree.Body        // BinaryExpression (GreaterThan)
tree.Parameters  // [ParameterExpression o]
tree.Compile()   // → Func<Order, bool>, if you want to run it
```

**EF Core requires expressions** because it converts them to SQL. Hand it a compiled `Func` and
it must fetch every row and filter in memory.

📂 [`Domain/Common/Specifications/ExpressionExtensions.cs`](../../src/LogiFlow.Domain/Common/Specifications/ExpressionExtensions.cs)

That file shows the naive approach and why it fails:

```csharp
// Compiles. Throws at query time — EF meets an opaque delegate invocation.
x => a.Compile()(x) && b.Compile()(x)
```

The correct combination rewrites one expression's tree so it uses the other's parameter node,
then joins the bodies with a single `AndAlso`. The rewriting is done with `ExpressionVisitor`,
the standard way to transform an expression tree — note it produces a *new* tree, because
expression trees are immutable.

---

## 5. Events, briefly

`event` is a delegate with restricted access: only the declaring type may invoke it; others may
only `+=` and `-=`.

Two things worth remembering:

- **`?.Invoke(...)`** — a null check and invoke in one, and thread-safe against a subscriber
  unsubscribing between the two.
- **Unsubscribe.** A long-lived publisher holding a delegate to a short-lived subscriber keeps it
  alive forever. This is the single most common managed memory leak in .NET.

This codebase uses **domain events** (module 05) rather than CLR events — they are a different
thing that happens to share a word.

---

## 6. Do the lab

```bash
dotnet test labs/Labs.Exercises --filter "FullyQualifiedName~Lab05"
```

Five exercises in [`labs/Labs.Exercises/Exercises/Lab05_Delegates.cs`](../../labs/Labs.Exercises/Exercises/Lab05_Delegates.cs):
write `Where` yourself, build a closure that caches, fix the `for`-loop capture bug, use a generic
constraint, and declare an event that outsiders cannot raise or wipe. The answers, with reasoning,
are in [SOLUTIONS.md](../SOLUTIONS.md).

---

## 7. Golden rules

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

---

## 8. Interview questions

**"What is a closure?"**
A lambda plus the variables it captured. The compiler lifts those variables into a generated
class so they outlive their original scope. It captures the *variable*, not its value.

**"Why does this print 3, 3, 3?"**
All three lambdas closed over the same `i` from the `for` loop, which is 3 after the loop. Fix by
copying into a loop-scoped local. Add that `foreach` was changed in C# 5 to allocate a fresh
variable per iteration — that detail signals you actually know the mechanism.

**"`Func` vs `Expression<Func>`?"**
`Func` is compiled code you can only invoke. `Expression<Func>` is a data structure describing it,
which a provider can translate — to SQL, for instance. Same lambda syntax; the target type
decides.

**"How would you combine two predicates for EF Core?"**
Not with `a.Compile()(x) && b.Compile()(x)` — EF cannot see through a delegate invocation.
Rewrite the second expression's parameter references to the first's using an `ExpressionVisitor`,
then combine the bodies with `Expression.AndAlso`.

**"When does a lambda allocate?"**
When it captures something. A capture-free lambda is cached in a static field and allocates once
per process. `static` lambdas (C# 9) enforce this at compile time.

---

## Next

→ [Module 03 — LINQ internals](../module-03-linq-internals/)
