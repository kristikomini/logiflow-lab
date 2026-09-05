namespace LogiFlow.Domain.Common;

/// <summary>
/// Something that has already happened inside the domain, stated in the past tense.
/// </summary>
/// <remarks>
/// <para>
/// <b>Naming is part of the contract.</b> <c>OrderPlacedDomainEvent</c>, not
/// <c>PlaceOrderEvent</c>. A command is a request that may be refused; an event is a
/// historical fact that cannot. If you find yourself wanting to "reject" an event, you
/// actually modelled a command.
/// </para>
/// <para>
/// <b>Why events at all?</b> Consider "when an order ships, email the customer, decrement
/// inventory, and notify the carrier". Putting those three calls inside
/// <c>Order.MarkShipped()</c> would drag SMTP, inventory, and HTTP clients into the Domain
/// layer — the exact dependency inversion this architecture exists to prevent. Instead
/// <c>MarkShipped()</c> records <c>ShipmentDispatchedDomainEvent</c> and returns. Handlers
/// living in the Application layer react to it.
/// </para>
/// <para>
/// <b>When do they fire?</b> Not when raised. They are collected on the aggregate and
/// dispatched by <c>DomainEventDispatchingInterceptor</c> during <c>SaveChangesAsync</c>,
/// inside the same transaction that persists the state change. Events for anything crossing
/// a process boundary go through the transactional outbox instead, so a committed order can
/// never fail to produce its integration event.
/// </para>
/// Covered in: <c>course/module-05-clean-architecture/04-domain-events.md</c>
/// and <c>course/module-06-efcore/07-outbox-pattern.md</c>
/// </remarks>
public interface IDomainEvent
{
    /// <summary>
    /// Unique identity of this specific occurrence — the idempotency key for consumers.
    /// </summary>
    Guid EventId { get; }

    /// <summary>UTC instant at which the fact became true.</summary>
    DateTimeOffset OccurredAtUtc { get; }
}

/// <summary>
/// Non-generic view of an aggregate's recorded events.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AggregateRoot{TId}"/> is generic over its identifier, so there is no single base
/// type the persistence layer can filter on. EF Core's change tracker hands back
/// <c>object</c>, and <c>OfType&lt;AggregateRoot&lt;?&gt;&gt;()</c> is not expressible in C#.
/// </para>
/// <para>
/// This interface is the answer: one non-generic contract every aggregate implements, so
/// <c>DomainEventDispatchingInterceptor</c> can write
/// <c>changeTracker.Entries().Select(e =&gt; e.Entity).OfType&lt;IHasDomainEvents&gt;()</c>.
/// The same trick as <c>ICommandMarker</c> in the Application layer — give a generic family a
/// non-generic base so runtime code can recognise it.
/// </para>
/// </remarks>
public interface IHasDomainEvents
{
    /// <summary>Events recorded but not yet dispatched.</summary>
    IReadOnlyList<IDomainEvent> DomainEvents { get; }

    /// <summary>Called once the events have been handed to the dispatcher.</summary>
    void ClearDomainEvents();
}

/// <summary>
/// Convenience base giving every event an ID and timestamp. Derive with a positional record:
/// <code>
/// public sealed record OrderPlacedDomainEvent(OrderId OrderId, Money Total) : DomainEventBase;
/// </code>
/// </summary>
public abstract record DomainEventBase : IDomainEvent
{
    /// <inheritdoc />
    public Guid EventId { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    /// <remarks>
    /// Defaulting to <see cref="DateTimeOffset.UtcNow"/> here is a deliberate, documented
    /// compromise: it makes every event site terser at the cost of one untestable clock read.
    /// Anywhere the timestamp is part of a business rule (SLA windows, cancellation deadlines),
    /// the value is passed in from <c>IDateTimeProvider</c> instead so tests can control it.
    /// </remarks>
    public DateTimeOffset OccurredAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
