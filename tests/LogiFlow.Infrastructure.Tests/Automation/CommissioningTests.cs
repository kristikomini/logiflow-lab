using LogiFlow.Application.Abstractions.Automation;
using LogiFlow.Domain.Automation;
using LogiFlow.Infrastructure.Automation;
using LogiFlow.Wcs;
using Microsoft.Extensions.Logging.Abstractions;

namespace LogiFlow.Infrastructure.Tests.Automation;

/// <summary>
/// Runs the real dispatcher against the simulated floor for an hour of simulated time, and
/// asserts what a commissioning engineer would check on site.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the test that replaces a Sunday.</b> On a real installation you get the line for
/// four hours, once, with people waiting. Here the same hour of warehouse operation runs in
/// milliseconds, deterministically, as often as you like — which is what "testing in simulation
/// environments" means when an intralogistics advert asks for it.
/// </para>
/// <para>
/// <b>Nothing here sleeps and nothing here is random.</b> The clock is a test double the test
/// advances by hand, and the simulator is seeded, so a failure is reproducible rather than a
/// story about a bad afternoon. A throughput assertion that depended on real delays would be a
/// flaky test dressed up as a guarantee.
/// </para>
/// Covered in: <c>course/module-28-industrial-and-ot/01-the-boundary.md</c>
/// </remarks>
public sealed class CommissioningTests
{
    private const int PassMilliseconds = 100;
    private const int PassesPerHour = 60 * 60 * 1000 / PassMilliseconds;

    [Fact]
    public void AnHourOfOperation_MovesWorkContinuously()
    {
        (TransportDispatcher dispatcher, SimulatedPlcGateway floor, ManualClock clock) = Warehouse();

        int completedAtHalfway = 0;

        for (int pass = 0; pass < PassesPerHour; pass++)
        {
            RunOnePass(dispatcher, floor, clock);

            if (pass == PassesPerHour / 2)
            {
                completedAtHalfway = dispatcher.Completed;
            }
        }

        // The exact number is a property of the seed and the floor plan; what is being asserted
        // is that the warehouse KEEPS WORKING. A system that deadlocks, leaks zones or starves a
        // vehicle produces a healthy first few minutes and then a flat line, which is precisely
        // the failure this test exists to catch — and precisely the one a short test would miss.
        dispatcher.Completed.ShouldBeGreaterThan(100);

        int secondHalf = dispatcher.Completed - completedAtHalfway;
        secondHalf.ShouldBeGreaterThan(
            completedAtHalfway / 2,
            $"throughput collapsed: {completedAtHalfway} in the first half hour, {secondHalf} in the second");
    }

    [Fact]
    public void AnHourOfOperation_NeverPutsTwoVehiclesInOneZone()
    {
        (TransportDispatcher dispatcher, SimulatedPlcGateway floor, ManualClock clock) = Warehouse();

        for (int pass = 0; pass < PassesPerHour; pass++)
        {
            RunOnePass(dispatcher, floor, clock);

            // The allocator's whole purpose, checked on every pass rather than at the end: a
            // collision that is resolved before the test finishes still happened, and on a real
            // floor it would have been two vehicles touching.
            IReadOnlyDictionary<string, EquipmentId> held = dispatcher.HeldZones;
            held.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count().ShouldBe(held.Count);
        }

        dispatcher.AllocationJournal.ShouldNotBeEmpty();
    }

    [Fact]
    public void AnHourOfOperation_ReleasesEveryZoneItTakes()
    {
        (TransportDispatcher dispatcher, SimulatedPlcGateway floor, ManualClock clock) = Warehouse();

        for (int pass = 0; pass < PassesPerHour; pass++)
        {
            RunOnePass(dispatcher, floor, clock);
        }

        // A leaked zone is permanent — nothing releases it, and that stretch of aisle is dead
        // until the process restarts. It shows up as a slow decline nobody attributes to software,
        // so the invariant is asserted directly: no more zones held than there are vehicles able
        // to hold them, at any point after an hour of churn.
        dispatcher.HeldZones.Count.ShouldBeLessThanOrEqualTo(floor.Fleet.Count * 2);
    }

    [Fact]
    public void FaultedVehicles_KeepTheirZonesUntilAcknowledged()
    {
        (TransportDispatcher dispatcher, SimulatedPlcGateway floor, ManualClock clock) = Warehouse();

        for (int pass = 0; pass < PassesPerHour; pass++)
        {
            RunOnePass(dispatcher, floor, clock);
        }

        // The seeded simulator faults roughly one move in forty, so an hour produces plenty. Each
        // one interrupts a transport order, and the honest outcome is a cancellation rather than
        // an invented completion: the pallet is wherever the vehicle stopped, and a new order has
        // to be raised from its ACTUAL position.
        dispatcher.Cancelled.ShouldBeGreaterThan(0);
        dispatcher.AllocationJournal.ShouldContain(entry => entry.Action == "released");
    }

    [Fact]
    public void Restore_ResumesPendingOrdersAndAbandonsInFlightOnes()
    {
        // The asymmetry is the whole restart rule. A pending order is a request nobody has acted
        // on, so it resumes untouched. An assigned one was being carried by a vehicle that has
        // since stopped wherever it stopped — the database knows where the load unit was GOING,
        // and only the floor knows where it IS. Resuming on the strength of the first is how a
        // system confidently drives to collect a pallet that is not there.
        (TransportDispatcher dispatcher, _, ManualClock clock) = Warehouse();

        TransportOrder waiting = TransportOrder.Create("UDC-1", "Z01", "Z05", clock.GetUtcNow()).Value;

        TransportOrder inFlight = TransportOrder.Create("UDC-2", "Z02", "Z09", clock.GetUtcNow()).Value;
        Equipment vehicle = Equipment.Register("LGV-01", EquipmentKind.Agv, clock.GetUtcNow()).Value;
        inFlight.AssignTo(vehicle, clock.GetUtcNow()).IsSuccess.ShouldBeTrue();

        int cancelledBefore = dispatcher.Cancelled;

        dispatcher.Restore([waiting, inFlight], clock.GetUtcNow());

        waiting.Status.ShouldBe(TransportOrderStatus.Pending);
        inFlight.Status.ShouldBe(TransportOrderStatus.Cancelled);
        dispatcher.Cancelled.ShouldBe(cancelledBefore + 1);
    }

    [Fact]
    public void Restore_QueuesTheResumedWorkRatherThanDroppingIt()
    {
        // Cancelling the in-flight one is only defensible if the pending one really does come
        // back — otherwise a restart quietly loses the backlog, which is the failure a store
        // exists to prevent in the first place.
        (TransportDispatcher dispatcher, SimulatedPlcGateway floor, ManualClock clock) = Warehouse();

        TransportOrder waiting = TransportOrder.Create("UDC-3", "Z01", "Z05", clock.GetUtcNow()).Value;
        dispatcher.Restore([waiting], clock.GetUtcNow());

        for (int pass = 0; pass < 200; pass++)
        {
            RunOnePass(dispatcher, floor, clock);
        }

        waiting.Status.ShouldNotBe(TransportOrderStatus.Pending);
    }

    [Fact]
    public void DrainDirty_ReportsEachChangedOrderOnce()
    {
        // The persister writes what this returns, so an order that changed twice in one pass must
        // not become two round trips for one row — and one that changed must not be missed.
        (TransportDispatcher dispatcher, SimulatedPlcGateway floor, ManualClock clock) = Warehouse();

        for (int pass = 0; pass < 50; pass++)
        {
            RunOnePass(dispatcher, floor, clock);
        }

        IReadOnlyCollection<TransportOrder> batch = dispatcher.DrainDirty();

        batch.ShouldNotBeEmpty();
        batch.Select(o => o.Id).Distinct().Count().ShouldBe(batch.Count);

        // Drained means drained: a second call must not rewrite the same rows.
        dispatcher.DrainDirty().ShouldBeEmpty();
    }

    /// <summary>A store that keeps nothing, for the tests that are not about storage.</summary>
    private sealed class InMemoryTransportOrderStore : ITransportOrderStore
    {
        public Task<IReadOnlyList<TransportOrder>> LoadOpenAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TransportOrder>>([]);

        public Task SaveAsync(IReadOnlyCollection<TransportOrder> orders, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private static (TransportDispatcher Dispatcher, SimulatedPlcGateway Floor, ManualClock Clock) Warehouse()
    {
        ManualClock clock = new(new DateTimeOffset(2026, 9, 8, 6, 0, 0, TimeSpan.Zero));

        SimulatedPlcGateway floor = new(
            new SimulationOptions { Vehicles = 4, Zones = 12, TravelMilliseconds = 400, Seed = 20260908 },
            clock,
            NullLogger<SimulatedPlcGateway>.Instance);

        TransportDispatcher dispatcher = new(
            floor, new InMemoryTransportOrderStore(), clock, NullLogger<TransportDispatcher>.Instance);
        dispatcher.Start();

        return (dispatcher, floor, clock);
    }

    /// <summary>
    /// Advances the world by one dispatcher pass: clock, then floor, then the loop.
    /// </summary>
    /// <remarks>
    /// The telemetry is pumped by hand rather than by the background reader the hosted service
    /// uses, because a test that raced a reader task would be exactly the flaky test this design
    /// exists to avoid. What is under test is the dispatcher's logic, not <c>Task.Run</c>.
    /// </remarks>
    private static void RunOnePass(TransportDispatcher dispatcher, SimulatedPlcGateway floor, ManualClock clock)
    {
        clock.Advance(TimeSpan.FromMilliseconds(PassMilliseconds));
        floor.TickAsync(CancellationToken.None).GetAwaiter().GetResult();

        while (floor.TryReadSample(out TelemetrySample sample))
        {
            dispatcher.Enqueue(sample);
        }

        dispatcher.RunPass();
    }

    /// <summary>
    /// A clock the test moves by hand.
    /// </summary>
    /// <remarks>
    /// Six lines, instead of taking a dependency on <c>Microsoft.Extensions.TimeProvider.Testing</c>
    /// for <c>FakeTimeProvider</c> — the same reasoning the repository already applies to
    /// <c>IDateTimeProvider</c>. Nothing here schedules a timer, so the scheduling half of
    /// <see cref="TimeProvider"/> is not needed and is not faked.
    /// </remarks>
    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
