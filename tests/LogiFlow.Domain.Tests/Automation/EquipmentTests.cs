using LogiFlow.Domain.Automation;
using LogiFlow.Domain.Automation.Events;
using LogiFlow.Domain.Results;

namespace LogiFlow.Domain.Tests.Automation;

/// <summary>
/// Tests for the machine state machine — in particular the transition that costs money if it is
/// wrong: leaving a latched fault.
/// </summary>
public sealed class EquipmentTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 8, 6, 0, 0, TimeSpan.Zero);

    private static Equipment AConveyor(string code = "CNV-12") =>
        Equipment.Register(code, EquipmentKind.Conveyor, T0).Value;

    [Fact]
    public void Register_WithoutACode_IsRejected()
    {
        // The code is what an electrician says on the phone at 03:00. A machine without one is a
        // GUID nobody can act on.
        Result<Equipment> result = Equipment.Register("   ", EquipmentKind.Agv, T0);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Automation.EquipmentCodeRequired");
    }

    [Fact]
    public void ObserveState_RepeatingTheCurrentState_RaisesNothing()
    {
        // A subscription delivers changes, but a reconnecting gateway re-sends the current value.
        // Treating that as a transition would produce a stop event of zero length every time the
        // network hiccuped, and availability would be quietly wrong.
        Equipment conveyor = AConveyor();
        conveyor.ObserveState(EquipmentState.Running, T0.AddSeconds(1));
        conveyor.ClearDomainEvents();

        Result repeat = conveyor.ObserveState(EquipmentState.Running, T0.AddSeconds(2));

        repeat.IsSuccess.ShouldBeTrue();
        conveyor.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void ObserveState_CarriesTheMachinesTimestampNotOurs()
    {
        // Availability is a sum of intervals between these instants. Using the receive time would
        // silently import network jitter into a number somebody reports to a plant manager.
        Equipment conveyor = AConveyor();
        DateTimeOffset atTheMachine = T0.AddSeconds(90);

        conveyor.ObserveState(EquipmentState.Running, atTheMachine);

        conveyor.StateChangedAtUtc.ShouldBe(atTheMachine);
        conveyor.DomainEvents
            .OfType<EquipmentStateChangedDomainEvent>()
            .Single()
            .SourceTimestampUtc.ShouldBe(atTheMachine);
    }

    [Fact]
    public void ObserveState_OlderThanTheLastObservation_IsRejected()
    {
        // A gateway that buffered through a dropped link flushes a burst afterwards, out of order.
        // Applying the old one would rewind the machine.
        Equipment conveyor = AConveyor();
        conveyor.ObserveState(EquipmentState.Running, T0.AddSeconds(10));

        Result stale = conveyor.ObserveState(EquipmentState.Idle, T0.AddSeconds(5));

        stale.IsFailure.ShouldBeTrue();
        stale.Error.Code.ShouldBe("Automation.ObservationOutOfOrder");
        conveyor.State.ShouldBe(EquipmentState.Running);
    }

    [Fact]
    public void ObserveState_FirstReadingSlightlyBeforeRegistration_IsAccepted()
    {
        // Registration is not an observation. A reading is already a scan cycle old when it
        // reaches us, so the first sample legitimately predates the moment we registered the
        // machine — and rejecting it would leave every machine stuck at Idle after a restart.
        Equipment conveyor = AConveyor();

        Result first = conveyor.ObserveState(EquipmentState.Running, T0.AddMilliseconds(-40));

        first.IsSuccess.ShouldBeTrue();
        conveyor.State.ShouldBe(EquipmentState.Running);
    }

    [Fact]
    public void AFaultedMachine_CannotSimplyStartRunningAgain()
    {
        // THE transition that matters. A fault is latched so that a machine which failed for a
        // reason nobody looked at cannot quietly resume — software that clears a fault by sending
        // a start command has removed a human checkpoint that exists on purpose.
        Equipment conveyor = AConveyor();
        conveyor.Fault("photocell obstructed", T0.AddSeconds(5));

        Result resume = conveyor.ObserveState(EquipmentState.Running, T0.AddSeconds(6));

        resume.IsFailure.ShouldBeTrue();
        resume.Error.Code.ShouldBe("Automation.FaultNotAcknowledged");
        conveyor.State.ShouldBe(EquipmentState.Faulted);
    }

    [Fact]
    public void Acknowledging_PermitsRunningAgainButDoesNotStartIt()
    {
        // Permission to run is not a start command, and collapsing the two is how a machine comes
        // back to life while somebody still has a hand in it.
        Equipment conveyor = AConveyor();
        conveyor.Fault("photocell obstructed", T0.AddSeconds(5));

        Result acknowledged = conveyor.Acknowledge("g.rossi", T0.AddSeconds(90));

        acknowledged.IsSuccess.ShouldBeTrue();
        conveyor.HasUnacknowledgedFault.ShouldBeFalse();
        conveyor.State.ShouldBe(EquipmentState.Faulted);

        conveyor.ObserveState(EquipmentState.Idle, T0.AddSeconds(95)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Acknowledging_RecordsWhoDidIt()
    {
        // This is the event an auditor reads.
        Equipment conveyor = AConveyor();
        conveyor.Fault("photocell obstructed", T0.AddSeconds(5));
        conveyor.ClearDomainEvents();

        conveyor.Acknowledge("  g.rossi  ", T0.AddSeconds(90));

        EquipmentFaultAcknowledgedDomainEvent audit = conveyor.DomainEvents
            .OfType<EquipmentFaultAcknowledgedDomainEvent>()
            .Single();

        audit.AcknowledgedBy.ShouldBe("g.rossi");
        audit.Reason.ShouldBe("photocell obstructed");
    }

    [Fact]
    public void Acknowledging_WithoutANamedPerson_IsRejected()
    {
        // Deliberately strict: the whole point of the record is that a person looked, and "system"
        // is not a person.
        Equipment conveyor = AConveyor();
        conveyor.Fault("photocell obstructed", T0.AddSeconds(5));

        Result anonymous = conveyor.Acknowledge(" ", T0.AddSeconds(90));

        anonymous.IsFailure.ShouldBeTrue();
        anonymous.Error.Code.ShouldBe("Automation.AcknowledgerRequired");
    }

    [Fact]
    public void RepeatingTheSameFault_IsNotASecondFault()
    {
        // A latched fault is re-reported on every poll by plenty of real devices. Counting each
        // one would turn a single stoppage into hundreds in the downtime report.
        Equipment conveyor = AConveyor();
        conveyor.Fault("photocell obstructed", T0.AddSeconds(5));
        conveyor.ClearDomainEvents();

        conveyor.Fault("photocell obstructed", T0.AddSeconds(6));

        conveyor.DomainEvents.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(EquipmentState.Blocked)]
    [InlineData(EquipmentState.Starved)]
    public void BlockedAndStarvedMachines_AreHealthyButCannotTakeWork(EquipmentState state)
    {
        // Neither is a fault — the machine is working perfectly and being punished for somebody
        // else's problem. But queueing more onto it makes the congestion worse, not better.
        Equipment conveyor = AConveyor();
        conveyor.ObserveState(state, T0.AddSeconds(5));

        conveyor.HasUnacknowledgedFault.ShouldBeFalse();
        conveyor.CanAcceptWork.ShouldBeFalse();
    }
}
