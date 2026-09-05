using LogiFlow.Domain.Common;

namespace LogiFlow.Domain.Customers.Events;

/// <summary>Raised when a customer moves between loyalty tiers.</summary>
/// <param name="CustomerId">The customer affected.</param>
/// <param name="PreviousTier">Tier before the change.</param>
/// <param name="NewTier">Tier after the change.</param>
public sealed record CustomerTierChangedDomainEvent(
    CustomerId CustomerId,
    CustomerTier PreviousTier,
    CustomerTier NewTier) : DomainEventBase
{
    /// <summary>True when the customer moved to a better tier.</summary>
    public bool IsUpgrade => NewTier > PreviousTier;
}
