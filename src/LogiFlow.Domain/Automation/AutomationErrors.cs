using LogiFlow.Domain.Results;

namespace LogiFlow.Domain.Automation;

/// <summary>Failure modes for the machine layer.</summary>
public static class AutomationErrors
{
    /// <summary>A machine was registered without the identifier painted on its cabinet.</summary>
    public static readonly Error EquipmentCodeRequired =
        Error.Validation("Automation.EquipmentCodeRequired", "Equipment must have a code, such as 'CNV-12'.");

    /// <summary>A fault was recorded with no explanation.</summary>
    public static readonly Error FaultReasonRequired =
        Error.Validation("Automation.FaultReasonRequired", "A fault must carry the reason the machine gave.");

    /// <summary>Somebody tried to clear a fault without saying who they were.</summary>
    /// <remarks>
    /// Deliberately strict. The whole point of the acknowledgement record is that a person
    /// looked, and "system" is not a person.
    /// </remarks>
    public static readonly Error AcknowledgerRequired =
        Error.Validation("Automation.AcknowledgerRequired", "Acknowledging a fault requires the name of the person clearing it.");

    /// <summary>A transport order was created with the same source and destination.</summary>
    public static readonly Error RouteRequired =
        Error.Validation("Automation.RouteRequired", "A transport order must move a load unit between two different zones.");

    /// <summary>A transport order was created with no load unit.</summary>
    public static readonly Error LoadUnitRequired =
        Error.Validation("Automation.LoadUnitRequired", "A transport order must name the load unit being moved.");

    /// <summary>A machine reported it left a fault that nobody has acknowledged.</summary>
    public static Error FaultNotAcknowledged(string code) =>
        Error.Conflict(
            "Automation.FaultNotAcknowledged",
            $"Equipment '{code}' has an unacknowledged fault and cannot leave the faulted state.");

    /// <summary>Nothing was latched, so there was nothing to clear.</summary>
    public static Error NothingToAcknowledge(string code) =>
        Error.Conflict("Automation.NothingToAcknowledge", $"Equipment '{code}' has no unacknowledged fault.");

    /// <summary>An observation arrived that is older than the one already applied.</summary>
    /// <remarks>
    /// Not a bug and not rare: a gateway that buffered through a dropped link flushes a burst
    /// afterwards, out of order. Reported as a failure so the caller can count it, because a
    /// rising count is a real signal about the link.
    /// </remarks>
    public static Error ObservationOutOfOrder(string code) =>
        Error.Conflict(
            "Automation.ObservationOutOfOrder",
            $"Observation for '{code}' is older than the state already recorded, so it was not applied.");

    /// <summary>Work was dispatched to a machine that cannot take it.</summary>
    public static Error CannotAcceptWork(string code, EquipmentState state) =>
        Error.Conflict("Automation.CannotAcceptWork", $"Equipment '{code}' is '{state}' and cannot accept work.");

    /// <summary>A transport order was moved out of a status it cannot leave.</summary>
    public static Error InvalidTransition(TransportOrderStatus from, TransportOrderStatus to) =>
        Error.Conflict("Automation.InvalidTransition", $"Cannot move a transport order from '{from}' to '{to}'.");
}
