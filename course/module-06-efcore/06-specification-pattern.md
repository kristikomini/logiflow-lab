# 6b. The specification pattern

> Part of [Module 06 — EF Core in depth](README.md), section 6.
> Previous: [6. Repositories and the unit of work](05-repositories-and-uow.md) ·
> Next: [7. Interceptors](08-interceptors.md)

---

[The previous chapter](05-repositories-and-uow.md) argued that a repository must not expose
`IQueryable`. That leaves a gap: real screens need arbitrary combinations of filters, and you cannot
write a method for each. `GetSubmittedOrdersForCustomerCreatedAfterAsync` is not a plan.

A **specification** is a named, composable, testable predicate that closes that gap without opening
the `IQueryable` door.

## The type

```csharp
public abstract class Specification<T>
{
    public abstract Expression<Func<T, bool>> ToExpression();

    public bool IsSatisfiedBy(T entity) => ToExpression().Compile()(entity);

    public Specification<T> And(Specification<T> other) => new AndSpecification<T>(this, other);
    public Specification<T> Or(Specification<T> other)  => new OrSpecification<T>(this, other);
    public Specification<T> Not()                       => new NotSpecification<T>(this);

    public static implicit operator Expression<Func<T, bool>>(Specification<T> spec) =>
        spec.ToExpression();
}
```

Everything follows from the return type of `ToExpression`. It is an
**`Expression<Func<T, bool>>` — an expression tree — not a `Func<T, bool>`.** That single choice is
what lets the same object be sent to the database *and* evaluated in memory.

## Why the expression tree, not a delegate

```csharp
Func<Order, bool>             predicate  = o => o.Status == OrderStatus.Submitted;   // opaque
Expression<Func<Order, bool>> expression = o => o.Status == OrderStatus.Submitted;   // inspectable
```

The first is compiled machine code. EF can do nothing with it except run it, which means loading the
whole table and filtering in memory — the client-side evaluation disaster from
[section 3](02-value-conversions.md).

The second is a **data structure describing the code**: a binary `Equal` node, a member access on
`Status`, a constant. EF's query provider walks that tree and emits `WHERE Status = 2`. The filter
runs on the database, over an index, and returns three rows instead of two million.

This is the `IEnumerable` versus `IQueryable` distinction from
[module 03](../module-03-linq-internals/) with a concrete consequence attached, and it is why the
composition operators below are the hard part.

## Composing without breaking translation

`And` cannot simply do `x => left(x) && right(x)` — that produces a tree containing an `Invoke` of a
compiled delegate, which EF cannot translate. The two operand trees also have **different
`ParameterExpression` instances**, and you cannot mix parameters from two trees.

The fix is a visitor that rewrites one side's parameter to match the other, in
[`ExpressionExtensions.cs`](../../src/LogiFlow.Domain/Common/Specifications/ExpressionExtensions.cs):

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

The result is a single tree whose body is an `AndAlso` node — translatable, indexable, and still one
query.

> **Lab 07 makes you write this.** `dotnet test labs/Labs.Exercises --filter "FullyQualifiedName~Lab07"`.
> The test asserts on `spec.Body.NodeType`, precisely so the `Compile()` shortcut cannot pass.

## Using them

```csharp
public static class OrderSpecifications
{
    public static Specification<Order> Submitted() =>
        new AdHocSpecification<Order>(o => o.Status == OrderStatus.Submitted);

    public static Specification<Order> ForCustomer(CustomerId id) =>
        new AdHocSpecification<Order>(o => o.CustomerId == id);

    public static Specification<Order> CreatedAfter(DateTimeOffset moment) =>
        new AdHocSpecification<Order>(o => o.CreatedAtUtc > moment);
}

// Composed at the call site, translated as one WHERE clause.
Specification<Order> spec = OrderSpecifications.Submitted()
    .And(OrderSpecifications.ForCustomer(customerId))
    .And(OrderSpecifications.CreatedAfter(lastWeek));

IReadOnlyList<Order> orders = await repository.ListAsync(spec, ct);
```

And in the repository, the implicit conversion means no ceremony:

```csharp
return await context.Orders
    .AsNoTracking()
    .Include(o => o.Lines)
    .Where(specification)        // Specification<Order> → Expression<Func<Order, bool>>
    .ToListAsync(ct);
```

## What it buys

**The rule is named once.** "Submitted" is a concept, not `Status == 2` written in nine places. When
the definition changes — submitted *or* confirmed, say — there is one edit.

**It is unit-testable without a database.** `IsSatisfiedBy` compiles the tree and runs it against an
in-memory object, so `OrderSpecifications.Submitted().IsSatisfiedBy(order)` is a millisecond test of
a business rule.

**The same rule works in both places.** The domain can ask "does this order satisfy the rule?" and
the database can ask "which rows satisfy it?" — from one definition. Duplicating a rule in C# and in
SQL is how the two drift.

**The repository stays closed.** `ListAsync(Specification<Order>)` accepts arbitrary filtering
without ever handing out `IQueryable`.

## The limits — say these in an interview

**It only expresses `WHERE`.** Ordering, paging, `Include` and projection are not in it. Some
implementations bolt them on (`Includes`, `OrderBy` collections on the specification); this one
deliberately does not, because a specification that also knows about eager loading is no longer a
predicate, it is a query object with a misleading name.

**Everything inside must be translatable.** A specification calling a C# helper method throws at
translation time, and the failure surfaces at the *call site*, far from the definition.

**It is overkill for one filter.** A repository method with two parameters is clearer than a
composed specification for a fixed query. Reach for it when combinations genuinely multiply.

## The mistakes

**`IsSatisfiedBy` in a loop.** It calls `Compile()` — which is expensive — on every invocation. For
in-memory filtering of a collection, compile once:
`var predicate = spec.ToExpression().Compile();`

**Composing with `&&` on compiled delegates.** Works in memory, silently drops to client-side
evaluation or throws against a database.

**Putting `Include` inside the specification.** Turns a predicate into a query object and couples the
domain to loading strategy.

**Specifications that reference other aggregates.** `o => o.Customer.Tier == Gold` requires a
navigation that [the mapping deliberately omits](03-fluent-configuration.md). If you need that, it is
a read-model query, not a specification.

## Try it

```bash
dotnet test labs/Labs.Exercises --filter "FullyQualifiedName~Lab07"
```

Implement `And` the wrong way first — `x => left.Compile()(x) && right.Compile()(x)` — and watch the
behavioural tests pass and the `NodeType` test fail. That gap between "produces the right answers"
and "produces the right SQL" is the entire chapter.

## What to remember

- A specification is a named predicate returning an **expression tree**, not a delegate.
- The tree is what lets EF translate it to SQL instead of filtering in memory.
- Composition needs a parameter-rewriting visitor; `&&` on compiled delegates breaks translation.
- One definition serves both the database and in-memory checks, so rules cannot drift.
- It keeps `IQueryable` out of the application layer while still allowing arbitrary filters.
- It expresses `WHERE` only — not ordering, paging or includes.
- `IsSatisfiedBy` compiles the tree. Never call it in a loop.

**Code:** [`Specification.cs`](../../src/LogiFlow.Domain/Common/Specifications/Specification.cs) ·
[`ExpressionExtensions.cs`](../../src/LogiFlow.Domain/Common/Specifications/ExpressionExtensions.cs) ·
[`OrderSpecifications.cs`](../../src/LogiFlow.Domain/Orders/OrderSpecifications.cs)

**Next:** [7. Interceptors](08-interceptors.md)
