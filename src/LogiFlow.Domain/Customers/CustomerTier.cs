namespace LogiFlow.Domain.Customers;

/// <summary>
/// Loyalty tier. Drives discount rate and free-shipping threshold.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why explicit numeric values?</b> Because these are persisted. If someone later inserts
/// <c>Bronze</c> alphabetically between <c>Gold</c> and <c>Silver</c> without pinning values,
/// every existing row silently changes meaning — every Gold customer becomes Bronze overnight.
/// Always assign explicit values to any enum that reaches a database or a wire format, and
/// never reuse a retired number.
/// </para>
/// <para>
/// This one <i>is</i> a plain enum rather than a smart enum like <see cref="ValueObjects.Currency"/>,
/// because the behaviour attached to it (see <see cref="CustomerTierExtensions"/>) is pricing
/// policy that belongs to the pricing rules, not intrinsic data about the tier itself.
/// Knowing when to reach for each is the actual skill.
/// </para>
/// </remarks>
public enum CustomerTier
{
    /// <summary>Default tier. No discount.</summary>
    Standard = 1,

    /// <summary>5% discount, free shipping over €100.</summary>
    Silver = 2,

    /// <summary>10% discount, free shipping over €50.</summary>
    Gold = 3,

    /// <summary>15% discount, always free shipping.</summary>
    Platinum = 4,
}

/// <summary>Pricing policy attached to a <see cref="CustomerTier"/>.</summary>
public static class CustomerTierExtensions
{
    /// <summary>The proportional discount for a tier, e.g. <c>0.10m</c> for Gold.</summary>
    /// <remarks>
    /// A <i>switch expression</i> over an enum, with no <c>default</c> arm on purpose.
    /// The compiler emits a warning when a new enum member is added and this switch stops
    /// being exhaustive — and because <c>src/Directory.Build.props</c> sets
    /// <c>TreatWarningsAsErrors</c>, that warning fails the build. Adding a tier without
    /// deciding its discount becomes impossible. Adding <c>default =&gt; 0m</c> would throw
    /// that safety net away, which is why it is absent.
    /// </remarks>
    public static decimal DiscountRate(this CustomerTier tier) => tier switch
    {
        CustomerTier.Standard => 0.00m,
        CustomerTier.Silver => 0.05m,
        CustomerTier.Gold => 0.10m,
        CustomerTier.Platinum => 0.15m,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown customer tier."),
    };

    /// <summary>Order subtotal in euros above which shipping is free. Zero means always free.</summary>
    public static decimal FreeShippingThresholdEur(this CustomerTier tier) => tier switch
    {
        CustomerTier.Standard => 150m,
        CustomerTier.Silver => 100m,
        CustomerTier.Gold => 50m,
        CustomerTier.Platinum => 0m,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown customer tier."),
    };
}
