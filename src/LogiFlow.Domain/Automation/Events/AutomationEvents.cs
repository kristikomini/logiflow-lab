using LogiFlow.Domain.Common;

namespace LogiFlow.Domain.Automation.Events;

/// <summary>Raised when a machine reports a different state from the one we had.</summary>
/// <param name="EquipmentId">The machine.</param>
/// <param name="Code">Its cabinet code, carried so a subscriber can log something readable.</param>
/// <param name="From">The state we had.</param>
/// <param name="To">The state it reports.</param>
/// <param name="SourceTimestampUtc">When the MACHINE says it changed — not when we heard.</param>
/// <remarks>
/// The stream of these events is what downtime and OEE are computed from, which is why the
/// timestamp carried here is the source one. Availability is a sum of intervals between these
/// instants, and using receive times would import network jitter into a number reported to a
/// plant manager. See <c>course/module-28-industrial-and-ot/05-oee-and-traceability.md</c>.
/// </remarks>
public sealed record EquipmentStateChangedDomainEvent(
    EquipmentId EquipmentId,
    string Code,
    EquipmentState From,
    EquipmentState To,
    DateTimeOffset SourceTimestampUtc) : DomainEventBase;

/// <summary>Raised when a machine latches a fault.</summary>
/// <param name="EquipmentId">The machine.</param>
/// <param name="Code">Its cabinet code.</param>
/// <param name="Reason">What the machine said, verbatim. It is evidence, so it is not tidied.</param>
/// <param name="SourceTimestampUtc">When the machine says it faulted.</param>
public sealed record EquipmentFaultedDomainEvent(
    EquipmentId EquipmentId,
    string Code,
    string Reason,
    DateTimeOffset SourceTimestampUtc) : DomainEventBase;

/// <summary>Raised when a person clears a latched fault.</summary>
/// <param name="EquipmentId">The machine.</param>
/// <param name="Code">Its cabinet code.</param>
/// <param name="Reason">The fault that was cleared.</param>
/// <param name="AcknowledgedBy">Who cleared it.</param>
/// <param name="AcknowledgedAtUtc">When.</param>
/// <remarks>
/// This is the event an auditor reads. It is separate from the state change on purpose:
/// acknowledging is permission to run again, not a start command, and the two happen at
/// different times and often on different people's authority.
/// </remarks>
public sealed record EquipmentFaultAcknowledgedDomainEvent(
    EquipmentId EquipmentId,
    string Code,
    string Reason,
    string AcknowledgedBy,
    DateTimeOffset AcknowledgedAtUtc) : DomainEventBase;

/// <summary>Raised when a transport order is given to a vehicle.</summary>
/// <param name="TransportOrderId">The order.</param>
/// <param name="EquipmentId">The vehicle that took it.</param>
/// <param name="AssignedAtUtc">When.</param>
public sealed record TransportOrderAssignedDomainEvent(
    TransportOrderId TransportOrderId,
    EquipmentId EquipmentId,
    DateTimeOffset AssignedAtUtc) : DomainEventBase;

/// <summary>Raised when a load unit reaches its destination.</summary>
/// <param name="TransportOrderId">The order.</param>
/// <param name="EquipmentId">The vehicle that carried it.</param>
/// <param name="LoadUnit">What was moved.</param>
/// <param name="CompletedAtUtc">When.</param>
public sealed record TransportOrderCompletedDomainEvent(
    TransportOrderId TransportOrderId,
    EquipmentId EquipmentId,
    string LoadUnit,
    DateTimeOffset CompletedAtUtc) : DomainEventBase;
