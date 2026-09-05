namespace LogiFlow.Domain.Common;

/// <summary>
/// Base class for anything with an identity that persists across changes to its data.
/// </summary>
/// <remarks>
/// <para>
/// <b>Entity vs Value Object</b> — the distinction that organises this whole layer:
/// </para>
/// <list type="bullet">
///   <item><description>
///     An <b>entity</b> is defined by its <i>identity</i>. Order #1234 is still Order #1234
///     after you change its shipping address. Two entities are equal iff their IDs match.
///   </description></item>
///   <item><description>
///     A <b>value object</b> is defined by its <i>attributes</i>. €10 is €10; there is no
///     "which ten euros". Two value objects are equal iff all their fields match.
///     See <see cref="LogiFlow.Domain.ValueObjects.Money"/>.
///   </description></item>
/// </list>
/// <para>
/// Getting this wrong is the classic beginner DDD mistake: modelling <c>Address</c> as an
/// entity with an <c>AddressId</c> produces a schema full of pointless join tables and code
/// that can't answer "are these two addresses the same?" without a database round trip.
/// </para>
/// Covered in: <c>course/module-05-clean-architecture/02-entities-and-value-objects.md</c>
/// </remarks>
/// <typeparam name="TId">The strongly-typed identifier.</typeparam>
public abstract class Entity<TId> : IEquatable<Entity<TId>>
    where TId : notnull
{
    /// <summary>The identity of this entity. Stable for the entity's whole lifetime.</summary>
    public TId Id { get; protected init; } = default!;

    /// <summary>Creates an entity with the given identity.</summary>
    protected Entity(TId id) => Id = id;

    /// <summary>
    /// Parameterless constructor for EF Core materialisation only.
    /// </summary>
    /// <remarks>
    /// EF Core creates instances via reflection when reading rows and does not go through your
    /// public factory methods. It can use a non-public constructor, which is why this is
    /// <c>protected</c> rather than <c>public</c> — application code still cannot build an
    /// entity in an invalid state, but the ORM can do its job.
    /// </remarks>
    protected Entity()
    {
    }

    /// <inheritdoc />
    public bool Equals(Entity<TId>? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        // Exact type check, NOT `is Entity<TId>`. Without this, an EF Core lazy-loading proxy
        // (OrderProxy : Order) would compare unequal to the Order it proxies — or worse, two
        // different subclasses sharing an ID would compare equal.
        return GetType() == other.GetType() && EqualityComparer<TId>.Default.Equals(Id, other.Id);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Entity<TId> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    /// <summary>Value equality by identity.</summary>
    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) =>
        left?.Equals(right) ?? right is null;

    /// <summary>Value inequality by identity.</summary>
    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !(left == right);
}
