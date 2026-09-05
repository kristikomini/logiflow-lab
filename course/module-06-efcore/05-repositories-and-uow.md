# 6. Repositories and the unit of work

> Part of [Module 06 — EF Core in depth](README.md), section 6.
> Previous: [4. The N+1 problem](04-n-plus-one.md) ·
> Next: [6b. The specification pattern](06-specification-pattern.md)

---

"Is the repository pattern an anti-pattern over EF Core?" is asked in interviews constantly, and the
answer that gets you the job is **"it depends what it wraps"** — followed by the reason.

The objection is real: `DbSet<T>` is already a repository, and `DbContext` is already a unit of work.
Wrapping them in `Repository<T>` that forwards `Add`, `Remove` and `Find` adds a layer and *removes*
capability, because you lose `Include`, projections, `AsSplitQuery` and everything else in
[section 4](04-n-plus-one.md).

## The generic repository, and why it fails

```csharp
// ✗ The version that earns the anti-pattern reputation.
public interface IRepository<T>
{
    Task<T?> GetByIdAsync(Guid id);
    Task<IEnumerable<T>> GetAllAsync();
    IQueryable<T> Query();            // ← the tell
    void Add(T entity);
    void Remove(T entity);
}
```

Two things are wrong with it.

**`GetAllAsync` on a table with two million rows** is an interface that invites a production
incident. Nothing in the signature discourages it.

**`IQueryable<T> Query()` leaks EF straight back out.** Callers now compose `Include`, `Where` and
`AsNoTracking` in the application layer — so the abstraction is not abstracting, and worse, whether a
query is translated to SQL or evaluated in memory now depends on the caller. You have the ceremony of
a repository with none of the containment.

## The version that earns its keep

```csharp
public interface IOrderRepository
{
    Task<Order?> GetAsync(OrderId id, CancellationToken ct = default);
    Task<Order?> GetWithLinesAsync(OrderId id, CancellationToken ct = default);
    Task<Order?> GetByNumberAsync(OrderNumber number, CancellationToken ct = default);
    Task<IReadOnlyList<Order>> ListAsync(Specification<Order> spec, CancellationToken ct = default);
    void Add(Order order);
}
```

Four properties make the difference:

**One per aggregate root, not one per entity.** There is no `IOrderLineRepository`, because an
`OrderLine` cannot be loaded or saved on its own — that is what
[the aggregate boundary](../module-05-clean-architecture/03-aggregates.md) means. The set of
repositories is therefore a readable list of your aggregates.

**Methods named in the domain's language.** `GetWithLinesAsync` says what you get and what it costs.
`GetByNumberAsync` is a real business lookup. Neither leaks EF.

**No `IQueryable` anywhere.** Query construction stays inside Infrastructure, where knowing about EF
is legitimate.

**It lives in the Application layer, and its implementation lives in Infrastructure.** That is
[dependency inversion](../module-05-clean-architecture/README.md#1-one-rule-dependencies-point-inward)
doing the only job it has: the arrow between the two projects runs Infrastructure → Application, and
`LayeringTests` fails the build if it ever reverses.

## What you actually get from it

Not "we could swap SQL Server for MongoDB" — nobody does that, and saying it in an interview is a
tell. Three real things:

**Handlers become testable without a database.** `CreateOrderCommandHandler` takes
`IOrderRepository`, and the unit test substitutes it. That is why
[`CreateOrderHandlerTests`](../../tests/LogiFlow.Application.Tests/Orders/CreateOrderHandlerTests.cs)
runs in milliseconds.

**Query construction has one home.** When `GetWithLinesAsync` needs `AsSplitQuery`, you change one
method rather than auditing every call site.

**The available operations are enumerable.** You can read `IOrderRepository` and know every way an
order can be fetched. With `IQueryable` exposed, the answer is "any way at all".

## The unit of work

```csharp
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> op, CancellationToken ct = default);
}
```

**Repositories do not save.** `Add` puts an entity in the change tracker; nothing is written until
`SaveChangesAsync`. That is the whole point: one business operation may touch several repositories
and must commit as one transaction. A repository that saved on every call would make that impossible
and would issue a round trip per entity.

Separating the two also means the *handler* decides the transaction boundary, which is where that
decision belongs — and in this repository the handler does not even do that, because
[`TransactionBehavior`](../module-08-cqrs/05-pipeline-behaviors.md) wraps every command.

### The part people get wrong: retries and transactions

```csharp
public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct)
{
    IExecutionStrategy strategy = context.Database.CreateExecutionStrategy();

    return await strategy.ExecuteAsync(async token =>
    {
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(token);
        T result = await operation(token);
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return result;
    }, ct);
}
```

The `CreateExecutionStrategy` wrapper is not decoration. With `EnableRetryOnFailure` configured — and
you want it, especially against Azure SQL — EF retries transient failures automatically. But a retry
inside a manual transaction is *unsafe*: EF would re-run the operation without knowing the
transaction was already begun, and you get a partially applied unit of work.

EF detects this and throws:

> The configured execution strategy 'SqlServerRetryingExecutionStrategy' does not support
> user-initiated transactions.

The fix is exactly the code above: put the **whole transaction inside the strategy**, so a retry
replays the entire block from `BeginTransaction`. It is a genuinely good interview answer, because it
shows you have run this against a cloud database rather than only localhost.

## When to skip the repository entirely

Be honest about this; it is the mark of judgement rather than loyalty.

**On the read side.** This repository does not route queries through repositories at all —
`IOrderQueries` and `IReportingQueries` project straight to DTOs with `AsNoTracking`, and their
implementations use EF or Dapper freely. A read has no invariants to protect, so the containment
buys nothing and costs projection flexibility. That is
[CQRS](../module-08-cqrs/01-what-cqrs-actually-is.md) in its most useful form.

**In an application with no domain rules.** Forms over data with nine tables does not need this. A
controller and a `DbContext` is the professional answer there, and four layers is cost with no
return.

## The mistakes

**`GenericRepository<T>` forwarding to `DbSet<T>`.** Adds indirection, removes capability. This is
the one the anti-pattern argument is about, and it is correct about it.

**Exposing `IQueryable`.** The abstraction stops abstracting and client-side evaluation becomes the
caller's problem.

**A repository per entity.** Announces that non-roots are independently loadable, which destroys the
boundary.

**Saving inside the repository.** Removes the caller's ability to make one transaction out of several
operations.

**Wrapping the unit of work around a transaction without an execution strategy.** Fine on localhost;
throws the moment retries are enabled.

## Try it

Open [`IRepositories.cs`](../../src/LogiFlow.Application/Abstractions/Data/IRepositories.cs) and note
that you can read every way an order can be loaded in about fifteen seconds. Then add
`IQueryable<Order> Query();` to the interface and try to write the architecture test that would stop
someone calling `.Include()` on it from a handler. You cannot — which is the argument.

## What to remember

- One repository per **aggregate root**. Never one per entity.
- Domain-named methods that state their cost. Never expose `IQueryable`.
- The interface lives in Application, the implementation in Infrastructure. That is DIP.
- Repositories do not save; the unit of work owns the transaction boundary.
- Wrap the whole transaction in `CreateExecutionStrategy` or retries break it.
- Skip repositories on the read side — queries project straight to DTOs.
- A generic `Repository<T>` over `DbSet<T>` genuinely is an anti-pattern. Say so, and say why.

**Code:** [`IRepositories.cs`](../../src/LogiFlow.Application/Abstractions/Data/IRepositories.cs) ·
[`OrderRepository.cs`](../../src/LogiFlow.Infrastructure/Persistence/Repositories/OrderRepository.cs) ·
[`UnitOfWork.cs`](../../src/LogiFlow.Infrastructure/Persistence/UnitOfWork.cs)

**Next:** [6b. The specification pattern](06-specification-pattern.md)
