using LogiFlow.Domain.Automation.Events;
using LogiFlow.Domain.Common;
using LogiFlow.Domain.Results;

namespace LogiFlow.Domain.Automation;

/// <summary>Where a transport order has got to.</summary>
public enum TransportOrderStatus
{
    /// <summary>Created, waiting for a vehicle.</summary>
    Pending = 0,

    /// <summary>A vehicle has it and is executing it.</summary>
    Assigned = 1,

    /// <summary>The load unit is at its destination.</summary>
    Completed = 2,

    /// <summary>Abandoned. The load unit is wherever it is, and a human needs to know.</summary>
    Cancelled = 3,
}

/// <summary>
/// <i>Move this load unit from A to B.</i> The unit of work a warehouse control system exists to
/// execute. Aggregate root.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is not just a row in a queue.</b> Because it has a lifecycle with rules — it can
/// be assigned once, completed only after being assigned, and cancelled from either of those but
/// never from <see cref="TransportOrderStatus.Completed"/>. Once the pallet is at its
/// destination, "cancelling" the order does not move it back; that would be a new order. This is
/// the same distinction as compensation-is-not-rollback in
/// <c>course/module-25-distributed-systems/05-sagas-and-eventual-consistency.md</c>, and here it
/// is physical rather than financial.
/// </para>
/// <para>
/// <b>Zones are strings, not entities.</b> <see cref="FromZone"/> and <see cref="ToZone"/> are
/// names from the floor plan, and the floor plan belongs to the customer, changes when they
/// re-rack an aisle, and does not need a row of its own for this aggregate to be correct. See
/// <see cref="ZoneAllocator"/> for where zone identity actually matters.
/// </para>
/// Covered in: <c>course/module-28-industrial-and-ot/04-traffic-and-deadlock.md</c>
/// </remarks>
public sealed class TransportOrder : AggregateRoot<TransportOrderId>
{
    private TransportOrder(
        TransportOrderId id,
        string loadUnit,
        string fromZone,
        string toZone,
        DateTimeOffset createdAtUtc) : base(id)
    {
        LoadUnit = loadUnit;
        FromZone = fromZone;
        ToZone = toZone;
        Status = TransportOrderStatus.Pending;
        CreatedAtUtc = createdAtUtc;
    }

    private TransportOrder()
    {
    }

    /// <summary>
    /// What is being moved — a pallet, a tote, a <i>bancale</i>. Free text on purpose: it is the
    /// label on the physical thing, and it is whatever the customer's system already calls it.
    /// </summary>
    public string LoadUnit { get; private set; } = null!;

    /// <summary>Where it is now.</summary>
    public string FromZone { get; private set; } = null!;

    /// <summary>Where it should end up.</summary>
    public string ToZone { get; private set; } = null!;

    /// <summary>Where the order has got to.</summary>
    public TransportOrderStatus Status { get; private set; }

    /// <summary>The vehicle executing it, once one has been assigned.</summary>
    public EquipmentId? AssignedTo { get; private set; }

    /// <summary>When the order was created.</summary>
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>When a vehicle took it, if one has.</summary>
    public DateTimeOffset? AssignedAtUtc { get; private set; }

    /// <summary>When it finished, if it has.</summary>
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    /// <summary>
    /// How long the order waited before a vehicle picked it up.
    /// </summary>
    /// <remarks>
    /// This is the number that exposes <b>starvation</b>. Total throughput can look healthy while
    /// one unlucky order waits twenty minutes because shorter routes keep overtaking it, and the
    /// aggregate figures will never show it. Watch the maximum of this, not the mean.
    /// </remarks>
    public TimeSpan? WaitedForVehicle => AssignedAtUtc - CreatedAtUtc;

    /// <summary>Raises a request to move a load unit.</summary>
    /// <param name="loadUnit">The label on the physical thing.</param>
    /// <param name="fromZone">Where it is.</param>
    /// <param name="toZone">Where it should be.</param>
    /// <param name="nowUtc">The current instant.</param>
    public static Result<TransportOrder> Create(string loadUnit, string fromZone, string toZone, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(loadUnit))
        {
            return AutomationErrors.LoadUnitRequired;
        }

        if (string.IsNullOrWhiteSpace(fromZone)
            || string.IsNullOrWhiteSpace(toZone)
            || string.Equals(fromZone, toZone, StringComparison.OrdinalIgnoreCase))
        {
            return AutomationErrors.RouteRequired;
        }

        return new TransportOrder(TransportOrderId.New(), loadUnit.Trim(), fromZone.Trim(), toZone.Trim(), nowUtc);
    }

    /// <summary>Gives the order to a vehicle.</summary>
    /// <remarks>
    /// Refuses a machine that cannot take work rather than dispatching and hoping. A conveyor
    /// that is <see cref="EquipmentState.Blocked"/> will not become less blocked by being given
    /// more to do, and a faulted one will simply not answer.
    /// </remarks>
    /// <param name="vehicle">The machine that will carry it.</param>
    /// <param name="nowUtc">The current instant.</param>
    public Result AssignTo(Equipment vehicle, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(vehicle);

        if (Status != TransportOrderStatus.Pending)
        {
            return AutomationErrors.InvalidTransition(Status, TransportOrderStatus.Assigned);
        }

        if (!vehicle.CanAcceptWork)
        {
            return AutomationErrors.CannotAcceptWork(vehicle.Code, vehicle.State);
        }

        Status = TransportOrderStatus.Assigned;
        AssignedTo = vehicle.Id;
        AssignedAtUtc = nowUtc;

        Raise(new TransportOrderAssignedDomainEvent(Id, vehicle.Id, nowUtc));
        return Result.Success();
    }

    /// <summary>Records that the load unit reached its destination.</summary>
    /// <param name="nowUtc">The current instant.</param>
    public Result Complete(DateTimeOffset nowUtc)
    {
        if (Status != TransportOrderStatus.Assigned)
        {
            return AutomationErrors.InvalidTransition(Status, TransportOrderStatus.Completed);
        }

        Status = TransportOrderStatus.Completed;
        CompletedAtUtc = nowUtc;

        Raise(new TransportOrderCompletedDomainEvent(Id, AssignedTo!.Value, LoadUnit, nowUtc));
        return Result.Success();
    }

    /// <summary>
    /// Abandons the order.
    /// </summary>
    /// <remarks>
    /// Refused once the order is <see cref="TransportOrderStatus.Completed"/>, because the pallet
    /// is physically at its destination and no status change will move it back. Wanting to undo a
    /// completed move means wanting a <i>new</i> transport order in the other direction, and
    /// making that explicit is what stops a cancelled-but-actually-delivered load unit becoming a
    /// pallet nobody can find.
    /// </remarks>
    /// <param name="nowUtc">The current instant.</param>
    public Result Cancel(DateTimeOffset nowUtc)
    {
        if (Status is TransportOrderStatus.Completed or TransportOrderStatus.Cancelled)
        {
            return AutomationErrors.InvalidTransition(Status, TransportOrderStatus.Cancelled);
        }

        Status = TransportOrderStatus.Cancelled;
        CompletedAtUtc = nowUtc;
        return Result.Success();
    }
}
