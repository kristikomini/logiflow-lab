using LogiFlow.Domain.Common;
using LogiFlow.Domain.ValueObjects;

namespace LogiFlow.Domain.Catalog.Events;

/// <summary>Raised when a product's list price changes to a genuinely different value.</summary>
/// <param name="ProductId">The product affected.</param>
/// <param name="Sku">The product's SKU, denormalised so consumers need no extra lookup.</param>
/// <param name="PreviousPrice">The price before the change.</param>
/// <param name="NewPrice">The price after the change.</param>
/// <remarks>
/// Including <paramref name="Sku"/> alongside <paramref name="ProductId"/> is deliberate
/// denormalisation. An event should carry enough context for a handler to act without querying
/// back — otherwise every handler generates a database round trip, and by the time it runs the
/// row may have changed again.
/// </remarks>
public sealed record ProductPriceChangedDomainEvent(
    ProductId ProductId,
    Sku Sku,
    Money PreviousPrice,
    Money NewPrice) : DomainEventBase
{
    /// <summary>Signed difference, positive for an increase.</summary>
    public Money Delta => NewPrice - PreviousPrice;

    /// <summary>True when the price went down — the case marketing cares about.</summary>
    public bool IsPriceDrop => NewPrice < PreviousPrice;
}

/// <summary>Raised when a product is withdrawn from sale.</summary>
/// <param name="ProductId">The product affected.</param>
/// <param name="Sku">The product's SKU.</param>
public sealed record ProductDiscontinuedDomainEvent(ProductId ProductId, Sku Sku) : DomainEventBase;
