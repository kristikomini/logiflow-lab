using System.Text.RegularExpressions;
using LogiFlow.Domain.Results;

namespace LogiFlow.Domain.Shipping;

/// <summary>Delivery company.</summary>
/// <remarks>Explicit values because this is persisted. See <see cref="Customers.CustomerTier"/>.</remarks>
public enum Carrier
{
    /// <summary>In-house van fleet, local deliveries only.</summary>
    OwnFleet = 1,

    /// <summary>DHL.</summary>
    Dhl = 2,

    /// <summary>UPS.</summary>
    Ups = 3,

    /// <summary>FedEx.</summary>
    FedEx = 4,

    /// <summary>Italian national postal service.</summary>
    PosteItaliane = 5,
}

/// <summary>Where a shipment is in the delivery process.</summary>
public enum ShipmentStatus
{
    /// <summary>Created, being packed. Not yet with the carrier.</summary>
    Preparing = 1,

    /// <summary>Handed over. Tracking number issued.</summary>
    Dispatched = 2,

    /// <summary>Scanned somewhere between origin and destination.</summary>
    InTransit = 3,

    /// <summary>Signed for. Terminal.</summary>
    Delivered = 4,

    /// <summary>Delivery attempted and failed; returning to sender. Terminal.</summary>
    Failed = 5,
}

/// <summary>
/// A carrier tracking reference, validated against that carrier's format.
/// </summary>
/// <remarks>
/// <para>
/// Each carrier uses a different reference format, and the validation is per-carrier rather
/// than one loose "any alphanumeric string" rule. Catching a mistyped tracking number at the
/// point of entry is far cheaper than discovering it when a customer clicks a dead tracking
/// link two days later.
/// </para>
/// <para>
/// The patterns here are simplified representatives of the real formats — enough to demonstrate
/// per-carrier parsing without pretending to be a carrier-integration library.
/// </para>
/// </remarks>
public readonly partial record struct TrackingNumber
{
    private TrackingNumber(string value, Carrier carrier)
    {
        Value = value;
        Carrier = carrier;
    }

    /// <summary>The reference itself.</summary>
    public string Value { get; }

    /// <summary>Which carrier issued it.</summary>
    public Carrier Carrier { get; }

    /// <summary>Validates a reference against its carrier's format.</summary>
    public static Result<TrackingNumber> Create(string? value, Carrier carrier)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Error.Validation("Tracking.Empty", "Tracking number is required.");
        }

        string normalised = value.Trim().ToUpperInvariant();

        bool valid = carrier switch
        {
            Carrier.OwnFleet => OwnFleetPattern().IsMatch(normalised),
            Carrier.Dhl => DhlPattern().IsMatch(normalised),
            Carrier.Ups => UpsPattern().IsMatch(normalised),
            Carrier.FedEx => FedExPattern().IsMatch(normalised),
            Carrier.PosteItaliane => PostePattern().IsMatch(normalised),
            _ => throw new ArgumentOutOfRangeException(nameof(carrier), carrier, "Unknown carrier."),
        };

        return valid
            ? new TrackingNumber(normalised, carrier)
            : Error.Validation(
                "Tracking.InvalidFormat",
                $"'{value}' is not a valid {carrier} tracking number.");
    }

    /// <summary>Rehydrates from trusted storage.</summary>
    public static TrackingNumber FromTrusted(string value, Carrier carrier) => new(value, carrier);

    /// <summary>Public tracking URL for this reference.</summary>
    public string TrackingUrl => Carrier switch
    {
        Carrier.OwnFleet => $"https://logiflow.example.com/track/{Value}",
        Carrier.Dhl => $"https://www.dhl.com/track?trackingNumber={Value}",
        Carrier.Ups => $"https://www.ups.com/track?tracknum={Value}",
        Carrier.FedEx => $"https://www.fedex.com/fedextrack/?trknbr={Value}",
        Carrier.PosteItaliane => $"https://www.poste.it/cerca/index.html#/risultati-spedizioni/{Value}",
        _ => throw new InvalidOperationException($"No tracking URL configured for carrier '{Carrier}'."),
    };

    /// <inheritdoc />
    public override string ToString() => Value;

    [GeneratedRegex(@"^LF-\d{10}$", RegexOptions.CultureInvariant)]
    private static partial Regex OwnFleetPattern();

    [GeneratedRegex(@"^\d{10}$", RegexOptions.CultureInvariant)]
    private static partial Regex DhlPattern();

    [GeneratedRegex(@"^1Z[0-9A-Z]{16}$", RegexOptions.CultureInvariant)]
    private static partial Regex UpsPattern();

    [GeneratedRegex(@"^\d{12}(\d{2})?$", RegexOptions.CultureInvariant)]
    private static partial Regex FedExPattern();

    [GeneratedRegex(@"^[A-Z]{2}\d{9}IT$", RegexOptions.CultureInvariant)]
    private static partial Regex PostePattern();
}
