namespace LogiFlow.Domain.Common;

/// <summary>
/// The entry point to a consistency boundary. Only aggregate roots get repositories.
/// </summary>
/// <remarks>
/// <para>
/// <b>An aggregate is a transactional boundary, not a folder.</b> Everything inside one
/// aggregate is saved together, atomically, and every invariant inside it is true at the
/// end of every transaction. <c>Order</c> is a root; <c>OrderLine</c> is not — you can never
/// load or save an <c>OrderLine</c> on its own, only through its <c>Order</c>. That is what
/// lets <c>Order</c> guarantee things like "total always equals the sum of the lines".
/// </para>
/// <para>
/// <b>The rule that beginners break:</b> aggregates reference each other <i>by ID</i>, never
/// by object reference. <c>Order</c> holds a <see cref="Customers.CustomerId"/>, not a
/// <c>Customer</c>. Break this and you get an object graph where loading one order drags half
/// the database into memory, and where two aggregates can be modified in one transaction —
/// which quietly turns your carefully designed boundaries into one giant lock.
/// </para>
/// <para>
/// <b>Sizing them:</b> small. If two pieces of data do not have to be consistent
/// <i>immediately</i>, they belong in different aggregates and can be reconciled with a domain
/// event. "The warehouse stock count must drop the instant the order is confirmed" is a claim
/// worth challenging — usually eventual consistency is both correct and much faster.
/// </para>
/// Covered in: <c>course/module-05-clean-architecture/03-aggregates.md</c>
/// </remarks>
/// <typeparam name="TId">The strongly-typed identifier of the root.</typeparam>
public abstract class AggregateRoot<TId> : Entity<TId>, IHasDomainEvents
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <inheritdoc cref="Entity{TId}(TId)" />
    protected AggregateRoot(TId id) : base(id)
    {
    }

    /// <inheritdoc cref="Entity{TId}()" />
    protected AggregateRoot()
    {
    }

    /// <inheritdoc />
    /// <remarks>
    /// Read-only to callers: nothing outside the aggregate is allowed to invent a fact about it.
    /// </remarks>
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>
    /// SQL Server <c>rowversion</c> column, used as an optimistic concurrency token.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The database bumps this automatically on every UPDATE. EF Core adds it to the WHERE
    /// clause, so a save becomes:
    /// </para>
    /// <code>UPDATE Orders SET Status = 2 WHERE Id = @id AND RowVersion = @originalRowVersion</code>
    /// <para>
    /// If another transaction wrote first, zero rows match and EF throws
    /// <c>DbUpdateConcurrencyException</c> — instead of silently discarding the other
    /// person's edit. This is the lost-update problem, and it is a genuinely common interview
    /// question. Optimistic (detect the clash) beats pessimistic (hold a lock) for typical web
    /// traffic because conflicts are rare and locks do not survive a stateless HTTP request.
    /// </para>
    /// Covered in: <c>course/module-07-sql-and-transactions/04-concurrency.md</c>
    /// </remarks>
    public byte[]? RowVersion { get; private set; }

    /// <summary>Records a fact. Call this from inside behaviour methods, never from outside.</summary>
    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    /// <inheritdoc />
    public void ClearDomainEvents() => _domainEvents.Clear();
}
