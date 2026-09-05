using LogiFlow.Domain.Results;

namespace LogiFlow.Domain.Shipping;

/// <summary>Failure modes for the <see cref="Shipment"/> aggregate.</summary>
public static class ShippingErrors
{
    /// <summary>Consignment had no weight.</summary>
    public static readonly Error WeightRequired =
        Error.Validation("Shipping.WeightRequired", "A shipment must have a positive total weight.");

    /// <summary>Transit scan had no location.</summary>
    public static readonly Error LocationRequired =
        Error.Validation("Shipping.LocationRequired", "A transit scan must record a location.");

    /// <summary>Failure recorded with no explanation.</summary>
    public static readonly Error FailureReasonRequired =
        Error.Validation("Shipping.FailureReasonRequired", "A reason is required when a delivery fails.");

    /// <summary>No shipment with that id.</summary>
    public static Error NotFound(ShipmentId id) =>
        Error.NotFound("Shipping.NotFound", $"No shipment found with id '{id}'.");

    /// <summary>Illegal status change.</summary>
    public static Error InvalidTransition(ShipmentStatus from, ShipmentStatus to) =>
        Error.Conflict("Shipping.InvalidTransition", $"Cannot move a shipment from '{from}' to '{to}'.");

    /// <summary>Shipment already reached a terminal status.</summary>
    public static Error AlreadyFinalised(ShipmentStatus status) =>
        Error.Conflict("Shipping.AlreadyFinalised", $"This shipment is already '{status}' and cannot be updated.");

    /// <summary>Tracking number belonged to a different carrier.</summary>
    public static Error CarrierMismatch(Carrier expected, Carrier actual) =>
        Error.Validation(
            "Shipping.CarrierMismatch",
            $"This shipment is booked with {expected} but the tracking number is a {actual} reference.");
}
