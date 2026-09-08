using LogiFlow.Domain.Automation;
using LogiFlow.Domain.Results;

namespace LogiFlow.Domain.Tests.Automation;

/// <summary>Tests for the unit of work a warehouse control system exists to execute.</summary>
public sealed class TransportOrderTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 8, 6, 0, 0, TimeSpan.Zero);

    private static TransportOrder AnOrder() =>
        TransportOrder.Create("UDC-100234", "Z01", "Z07", T0).Value;

    private static Equipment AVehicle() =>
        Equipment.Register("LGV-04", EquipmentKind.Agv, T0).Value;

    [Fact]
    public void Create_WithTheSameSourceAndDestination_IsRejected()
    {
        Result<TransportOrder> nowhere = TransportOrder.Create("UDC-1", "Z01", "z01", T0);

        nowhere.IsFailure.ShouldBeTrue();
        nowhere.Error.Code.ShouldBe("Automation.RouteRequired");
    }

    [Fact]
    public void AssignTo_AFaultedVehicle_IsRejected()
    {
        // Refuse rather than dispatch and hope. A faulted vehicle will simply not answer, and the
        // order would sit "assigned" to something that is not moving.
        TransportOrder order = AnOrder();
        Equipment vehicle = AVehicle();
        vehicle.Fault("photocell obstructed", T0.AddSeconds(1));

        Result assigned = order.AssignTo(vehicle, T0.AddSeconds(2));

        assigned.IsFailure.ShouldBeTrue();
        assigned.Error.Code.ShouldBe("Automation.CannotAcceptWork");
        order.Status.ShouldBe(TransportOrderStatus.Pending);
    }

    [Fact]
    public void AssignTo_Twice_IsRejected()
    {
        TransportOrder order = AnOrder();
        order.AssignTo(AVehicle(), T0.AddSeconds(1));

        Result again = order.AssignTo(AVehicle(), T0.AddSeconds(2));

        again.IsFailure.ShouldBeTrue();
        again.Error.Code.ShouldBe("Automation.InvalidTransition");
    }

    [Fact]
    public void Complete_BeforeBeingAssigned_IsRejected()
    {
        Result completed = AnOrder().Complete(T0.AddSeconds(1));

        completed.IsFailure.ShouldBeTrue();
        completed.Error.Code.ShouldBe("Automation.InvalidTransition");
    }

    [Fact]
    public void Cancel_AfterCompletion_IsRejected()
    {
        // The pallet is physically at its destination and no status change will move it back.
        // Wanting to undo a completed move means wanting a NEW order in the other direction, and
        // making that explicit is what stops a cancelled-but-delivered load unit becoming a pallet
        // nobody can find.
        TransportOrder order = AnOrder();
        order.AssignTo(AVehicle(), T0.AddSeconds(1));
        order.Complete(T0.AddSeconds(30));

        Result undo = order.Cancel(T0.AddSeconds(40));

        undo.IsFailure.ShouldBeTrue();
        undo.Error.Code.ShouldBe("Automation.InvalidTransition");
        order.Status.ShouldBe(TransportOrderStatus.Completed);
    }

    [Fact]
    public void WaitedForVehicle_IsTheNumberThatExposesStarvation()
    {
        // Total throughput can look healthy while one unlucky order waits twenty minutes because
        // shorter routes keep overtaking it. Watch the maximum of this, never the mean.
        TransportOrder order = AnOrder();
        order.AssignTo(AVehicle(), T0.AddMinutes(20));

        order.WaitedForVehicle.ShouldBe(TimeSpan.FromMinutes(20));
    }
}
