using LogiFlow.Domain.Automation;

namespace LogiFlow.Domain.Tests.Automation;

/// <summary>
/// Tests for the traffic manager — the one part of a WCS with a genuinely interesting failure
/// mode, and the one an interviewer at an intralogistics company will enjoy.
/// </summary>
public sealed class ZoneAllocatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 8, 6, 0, 0, TimeSpan.Zero);

    private static EquipmentId Vehicle() => EquipmentId.New();

    [Fact]
    public void TwoVehiclesWantingOppositeRoutes_CannotDeadlock()
    {
        // The classic: V1 goes 0→1, V2 goes 1→0. Acquired one zone at a time in route order,
        // this is a guaranteed cycle in the wait-for graph and both vehicles stop forever.
        ZoneAllocator allocator = new();
        EquipmentId v1 = Vehicle();
        EquipmentId v2 = Vehicle();

        Allocation first = allocator.Request(v1, ["Z01", "Z02"], T0);
        Allocation second = allocator.Request(v2, ["Z02", "Z01"], T0);

        // All-or-nothing is what removes the cycle: V2 holds NOTHING while it waits, so it cannot
        // be half of a deadlock. It is blocked, which is a different thing entirely — blocked
        // resolves the moment V1 finishes, and deadlock never resolves at all.
        first.IsGranted.ShouldBeTrue();
        second.IsGranted.ShouldBeFalse();
        allocator.HeldZones.Values.ShouldAllBe(holder => holder.Equals(v1));

        IReadOnlyList<EquipmentId> served = allocator.Release(v1, T0.AddSeconds(1));

        served.ShouldBe([v2]);
        allocator.HeldZones.Count.ShouldBe(2);
    }

    [Fact]
    public void AQueuedRequest_IsNotOvertakenByALaterOne()
    {
        // Starvation is worse than deadlock in one specific way: deadlock is obvious and this is
        // not. Throughput stays healthy, nobody raises a ticket, and one vehicle sits in the same
        // spot for twenty minutes until a shift supervisor mentions it in passing.
        ZoneAllocator allocator = new();
        EquipmentId holder = Vehicle();
        EquipmentId waiting = Vehicle();
        EquipmentId latecomer = Vehicle();

        allocator.Request(holder, ["Z01"], T0);
        allocator.Request(waiting, ["Z01", "Z02"], T0.AddSeconds(1));

        // Z02 is free, so a naive allocator hands it over and the waiting vehicle keeps losing.
        Allocation overtaking = allocator.Request(latecomer, ["Z02"], T0.AddSeconds(2));

        overtaking.IsGranted.ShouldBeFalse();
    }

    [Fact]
    public void ARequestBehindAnUnrelatedOne_IsNotHeldUpByIt()
    {
        // The other half of the same rule. Strict FIFO would be fair and useless: one vehicle
        // waiting for a busy aisle would stop every unrelated move in the building.
        ZoneAllocator allocator = new();
        EquipmentId holder = Vehicle();
        EquipmentId blocked = Vehicle();
        EquipmentId unrelated = Vehicle();

        allocator.Request(holder, ["Z01"], T0);
        allocator.Request(blocked, ["Z01"], T0.AddSeconds(1));

        Allocation elsewhere = allocator.Request(unrelated, ["Z09", "Z10"], T0.AddSeconds(2));

        elsewhere.IsGranted.ShouldBeTrue();
    }

    [Fact]
    public void AskingWhileHolding_IsRefused()
    {
        // Hold-and-wait is the one route back to deadlock, so it is refused rather than tolerated.
        ZoneAllocator allocator = new();
        EquipmentId vehicle = Vehicle();

        allocator.Request(vehicle, ["Z01"], T0);
        Allocation again = allocator.Request(vehicle, ["Z02"], T0.AddSeconds(1));

        again.Outcome.ShouldBe(AllocationOutcome.AlreadyHolding);
    }

    [Fact]
    public void ZoneNames_AreComparedWithoutCase()
    {
        // They come from a floor plan drawn by a person. AISLE-3 and Aisle-3 being two different
        // zones is two vehicles in one aisle.
        ZoneAllocator allocator = new();

        allocator.Request(Vehicle(), ["Aisle-3"], T0);
        Allocation clash = allocator.Request(Vehicle(), ["AISLE-3"], T0);

        clash.IsGranted.ShouldBeFalse();
    }

    [Fact]
    public void ReleasingAVehicleThatHoldsNothing_IsHarmless()
    {
        // A dispatcher unwinding after a fault does not always know what was granted. Making it
        // find out first would be an invitation to leak zones on the error path, and a leaked zone
        // is permanent — that stretch of aisle is dead until the process restarts.
        ZoneAllocator allocator = new();

        Should.NotThrow(() => allocator.Release(Vehicle(), T0));
    }

    [Fact]
    public void RebuildFromFloor_ReplacesWhateverWeThoughtWeKnew()
    {
        // After a crash the vehicles are where they are, holding real space, whatever this process
        // believes. Coming back with a stale table and granting from it puts two vehicles in one
        // aisle while the software is convinced everything is fine — the failure mode with a
        // safety dimension rather than an availability one.
        ZoneAllocator allocator = new();
        EquipmentId ghost = Vehicle();
        EquipmentId actual = Vehicle();

        allocator.Request(ghost, ["Z01", "Z02"], T0);
        allocator.RebuildFromFloor([new KeyValuePair<string, EquipmentId>("Z07", actual)], T0.AddMinutes(5));

        allocator.HeldZones.Count.ShouldBe(1);
        allocator.HeldZones["Z07"].ShouldBe(actual);
        allocator.QueueLength.ShouldBe(0);
    }

    [Fact]
    public void EveryDecision_IsJournalled()
    {
        // "Why did vehicle 7 wait four minutes" is a question you will be asked, more than once,
        // and the journal is the only thing that can answer it.
        ZoneAllocator allocator = new();
        EquipmentId vehicle = Vehicle();

        allocator.Request(vehicle, ["Z01"], T0);
        allocator.Release(vehicle, T0.AddSeconds(30));

        allocator.Journal.Select(entry => entry.Action).ShouldBe(["granted", "released"]);
    }
}
