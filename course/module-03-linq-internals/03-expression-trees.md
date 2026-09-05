# 4. Expression trees, and what EF Core can translate

> Part of [Module 03 — LINQ internals](README.md), section 4.
> Previous: [3. `yield return`](README.md#3-yield-return--how-lazy-operators-are-built) ·
> Next: [5. Composition](README.md#5-composition--the-pattern-worth-stealing)

---

Two lines that look identical and are not the same kind of thing at all:

```csharp
Func<Order, bool>             predicate  = o => o.Status == OrderStatus.Submitted;
Expression<Func<Order, bool>> expression = o => o.Status == OrderStatus.Submitted;
```

The first is **compiled code**. You can call it; you cannot look inside it.

The second is a **data structure describing that code** — a tree of nodes the compiler built instead
of emitting IL:

```
Lambda
└── Equal                       ← BinaryExpression, NodeType = Equal
    ├── MemberAccess: Status    ← on the parameter `o`
    └── Constant: 2
```

You can walk it, rewrite it, and — crucially — **translate it into another language**. That is the
whole mechanism behind `IQueryable`, and the difference between a query that runs on the database and
one that loads your table into memory.

## Why this decides where your query runs

```csharp
IEnumerable<Order> memory = db.Orders.AsEnumerable();
memory.Where(o => o.Total.Amount > 1000);      // Func — LINQ to Objects, in memory

IQueryable<Order> queryable = db.Orders;
queryable.Where(o => o.Total.Amount > 1000);   // Expression — EF walks the tree, emits SQL
```

`Enumerable.Where` takes a `Func<T, bool>`. `Queryable.Where` takes an
`Expression<Func<T, bool>>`. Same call syntax, different overload chosen by the static type of the
source — which is why a stray `.AsEnumerable()` or `.ToList()` in the middle of a query silently
moves the filter from SQL Server to your process.

The failure is not an error. It is two million rows over the wire and a `WHERE` clause applied in
memory, and it passes every test that runs on 200 rows.

## What cannot be translated

EF's provider walks the tree and maps nodes to SQL. It can map property access, comparisons,
arithmetic, `string.Contains`, `Any`, `Sum`, and a long list of known methods. It cannot map anything
whose body it cannot see:

```csharp
// ✗ A call to your own method. The tree contains a MethodCall node EF has never heard of.
db.Orders.Where(o => IsInteresting(o))

// ✗ A compiled delegate invoked inside the tree.
db.Orders.Where(o => predicate(o))

// ✗ A property on a value-converted type — EF cannot see through the conversion.
db.Orders.Where(o => o.OrderNumber.Value.StartsWith("ORD-2026"))
```

Modern EF Core **throws** on these rather than silently evaluating them client-side. That default
changed in EF Core 3.0 and it was the right call: a loud failure beats a query that works and is a
thousand times slower.

The general rule: **if EF cannot see inside it, EF cannot translate it.** A lambda body may only
mention things the provider understands.

## Composing without breaking translation

You cannot combine two expression trees with `&&`, because that operates on `bool` values, not on
trees. And you cannot naively glue their bodies together either — each lambda has its **own
`ParameterExpression` instance**, and a tree referencing a parameter its lambda does not declare
throws.

The standard solution is a visitor that rewrites one side's parameter to match the other:

```csharp
public static Expression<Func<T, bool>> And<T>(
    this Expression<Func<T, bool>> left,
    Expression<Func<T, bool>> right)
{
    ParameterExpression parameter = left.Parameters[0];
    Expression rewritten = new ParameterReplacer(right.Parameters[0], parameter).Visit(right.Body);

    return Expression.Lambda<Func<T, bool>>(Expression.AndAlso(left.Body, rewritten), parameter);
}

private sealed class ParameterReplacer(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
{
    protected override Expression VisitParameter(ParameterExpression node) =>
        node == from ? to : base.VisitParameter(node);
}
```

`ExpressionVisitor` is the framework's own tool for rewriting trees: override the `Visit*` method for
the node type you care about and it walks everything else for you.

The result is a single tree whose body is an `AndAlso` node — one `WHERE` clause, one index seek.
This is what [the specification pattern](../module-06-efcore/06-specification-pattern.md) is built
on.

**The tempting wrong version:**

```csharp
// Passes every behavioural test. Untranslatable.
return x => left.Compile()(x) && right.Compile()(x);
```

It produces correct answers in memory, and the tree now contains an `Invoke` of a compiled delegate,
so against a database it either throws or loads the table. Lab 07 asserts on `Body.NodeType`
specifically to catch it.

## Building a tree by hand

Occasionally you need one the compiler cannot write for you — sorting by a column name chosen at
runtime, for example:

```csharp
public static IQueryable<T> OrderByProperty<T>(this IQueryable<T> source, string propertyName)
{
    ParameterExpression parameter = Expression.Parameter(typeof(T), "x");
    MemberExpression property = Expression.Property(parameter, propertyName);   // throws if unknown
    LambdaExpression selector = Expression.Lambda(property, parameter);

    MethodCallExpression call = Expression.Call(
        typeof(Queryable), nameof(Queryable.OrderBy), [typeof(T), property.Type],
        source.Expression, Expression.Quote(selector));

    return source.Provider.CreateQuery<T>(call);
}
```

**`Expression.Property` throwing on an unknown name is the security control.** The alternative —
concatenating the name into SQL — is injection. Even so, prefer an allow-list of sortable columns:
this version leaks which properties exist through its exception messages, and lets a caller sort by
an unindexed column and table-scan your database.

[Module 09 section 7](../module-09-advanced-linq/05-dynamic-predicates.md) is the practical version.

## Costs

**Building is slow.** Constructing and translating a tree is far more expensive than calling a
delegate. EF caches compiled query plans keyed on the tree's shape, which is why parameters matter:
`Where(o => o.Id == id)` reuses a plan, while a tree built fresh each time with a *constant* baked in
does not.

**`Compile()` is very slow.** Roughly microseconds versus nanoseconds, and it allocates. Never call
it in a loop — compile once and reuse the delegate. `IsSatisfiedBy` on a specification compiles every
call, which is fine for one check and wrong for filtering ten thousand objects.

## The mistakes

**A stray `AsEnumerable()` or `ToList()` mid-query.** Everything after it runs in memory.

**Calling your own method inside a query.** Untranslatable; the fix is to inline the logic or make it
a composed expression.

**Composing with `Compile()`.** Correct answers, wrong execution location.

**`Compile()` in a loop.** Microseconds each, and it allocates.

**Interpolating a property name into SQL** instead of building a tree. Injection.

## Try it

```bash
dotnet run --project labs/Labs.Playground expressions
```

It prints the tree for a simple lambda node by node, then the SQL EF generates from it. Then add
`.AsEnumerable()` before a `.Where(...)` in one of the queries in
[`OrderQueries.cs`](../../src/LogiFlow.Infrastructure/Persistence/Queries/OrderQueries.cs), turn on
SQL logging, and watch the `WHERE` clause vanish from the generated SQL while the results stay
correct. That silent move is the thing to be able to spot in review.

## What to remember

- `Func<T,bool>` is code; `Expression<Func<T,bool>>` is a data structure describing code.
- `Enumerable.Where` takes the first, `Queryable.Where` the second — chosen by the source's static type.
- A stray `AsEnumerable()` moves filtering from the database into your process, silently.
- EF can only translate what it can see inside. Your own methods and compiled delegates cannot be.
- Compose with an `ExpressionVisitor` that rewrites the parameter, never with `Compile()`.
- Build trees by hand for runtime-chosen members — and allow-list the names.
- `Compile()` is expensive. Once, not per item.

**Code:** [`ExpressionExtensions.cs`](../../src/LogiFlow.Domain/Common/Specifications/ExpressionExtensions.cs) ·
[`Specification.cs`](../../src/LogiFlow.Domain/Common/Specifications/Specification.cs) ·
[`Lab07_Specifications.cs`](../../labs/Labs.Exercises/Exercises/Lab07_Specifications.cs)

**Back to:** [Module 03](README.md)
