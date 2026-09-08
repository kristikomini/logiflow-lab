using System.Collections.Concurrent;
using LogiFlow.Application.Abstractions.Automation;
using LogiFlow.Domain.Automation;
using LogiFlow.Domain.Results;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LogiFlow.Wcs;

/// <summary>
/// The warehouse control system's one loop: take transport orders, find a vehicle, get it a route
/// nobody else holds, send the move, and listen for what the floor says happened.
/// </summary>
/// <remarks>
/// <para>
/// <b>Everything that touches shared state happens on this one loop, on purpose.</b> The
/// <see cref="ZoneAllocator"/> is not thread-safe, and it is not meant to be: you are granting
/// tens of routes a second, not millions, so a single component that owns the whole zone table
/// and processes requests in order is trivially fair, trivially auditable, and several orders of
/// magnitude faster than it needs to be. Reaching for locks or a distributed allocator here is a
/// self-inflicted wound.
/// </para>
/// <para>
/// Anything that happens off the loop — telemetry arriving, a command being refused by a machine
/// — is <i>queued</i> rather than applied where it lands, and drained at the top of the next pass.
/// That is what keeps the whole state machine single-threaded without making the gateway wait.
/// </para>
/// <para>
/// <b>What survives a restart, and what deliberately does not.</b> Transport orders are stored,
/// because somebody asked for them and is waiting for the answer. The fleet is not — it comes from
/// the gateway's commissioning data. Zone occupancy is not, and must not be: after a crash the
/// vehicles are physically where they are, so the allocator rebuilds from what the floor reports
/// rather than from anything this process last wrote. See
/// <see cref="ZoneAllocator.RebuildFromFloor"/>, which is the half a database does not solve.
/// </para>
/// <para>
/// <b>And an order that was in flight does not survive either</b> — see <see cref="Restore"/>. It
/// is the one restart rule worth reading twice.
/// </para>
/// Covered in: <c>course/module-28-industrial-and-ot/04-traffic-and-deadlock.md</c>
/// </remarks>
/// <param name="gateway">The machine layer.</param>
/// <param name="store">Durable storage for the orders. Scope-per-call; see ScopedTransportOrderStore.</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="logger">Logger.</param>
public sealed class TransportDispatcher(
    IEquipmentGateway gateway,
    ITransportOrderStore store,
    TimeProvider timeProvider,
    ILogger<TransportDispatcher> logger) : BackgroundService
{
    private static readonly TimeSpan PassInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// How long a latched fault sits before it is cleared.
    /// </summary>
    /// <remarks>
    /// <b>This stands in for a person, and it is the one piece of make-believe in this class.</b>
    /// On a real floor a technician walks over, looks at why the photocell was obstructed, and
    /// acknowledges at the HMI — deliberately, because a fault that clears itself is a fault
    /// nobody investigated. Modelling that as a timer lets an unattended demonstration keep
    /// running; it must never become the production behaviour.
    /// </remarks>
    private static readonly TimeSpan TechnicianResponse = TimeSpan.FromSeconds(30);

    private readonly ConcurrentQueue<TelemetrySample> _inbox = new();
    private readonly ConcurrentQueue<EquipmentId> _refused = new();

    /* Orders whose state has moved since the last save. A set rather than a queue: an order can
       change twice in one pass — created, then assigned — and writing it twice would be two round
       trips for one row. Guarded by its own lock because the persister drains it off the loop. */
    private readonly HashSet<TransportOrder> _dirty = [];
    private readonly Lock _dirtyLock = new();
    private readonly Dictionary<EquipmentId, Equipment> _fleet = [];
    private readonly Dictionary<EquipmentId, TransportOrder> _inFlight = [];
    private readonly Dictionary<EquipmentId, TransportOrder> _awaitingRoute = [];
    private readonly Queue<TransportOrder> _pending = new();
    private readonly ZoneAllocator _allocator = new();
    private readonly Random _random = new(20260908);

    private string[] _zones = [];
    private DateTimeOffset _lastHeartbeat;
    private int _completed;
    private int _cancelled;
    private int _rejectedOutOfOrder;

    /// <summary>Transport orders finished. Read by the commissioning tests.</summary>
    public int Completed => _completed;

    /// <summary>Transport orders abandoned because the vehicle carrying them faulted.</summary>
    public int Cancelled => _cancelled;

    /// <summary>Observations discarded because they were older than one already applied.</summary>
    /// <remarks>
    /// Not an error count. A gateway that buffered through a dropped link flushes a burst out of
    /// order afterwards, so a few are normal — but a <i>rising</i> rate is a real signal about the
    /// link, and it is only visible because somebody kept the number.
    /// </remarks>
    public int RejectedOutOfOrder => _rejectedOutOfOrder;

    /// <summary>The allocator's journal — every grant, refusal and release, in order.</summary>
    public IReadOnlyList<AllocationJournalEntry> AllocationJournal => _allocator.Journal;

    /// <summary>Zones currently held, and by which vehicle.</summary>
    public IReadOnlyDictionary<string, EquipmentId> HeldZones => _allocator.HeldZones;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Start();

        try
        {
            Restore(await store.LoadOpenAsync(stoppingToken).ConfigureAwait(false), timeProvider.GetUtcNow());
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            /* A WCS that will not start because a database is unreachable is a warehouse that
               cannot move a pallet, and the pallets are the point. Losing the backlog is bad;
               refusing to dispatch anything at all is worse, so this degrades to an empty queue
               and says so loudly rather than taking the host down with it. */
            logger.LogError(e, "Could not restore transport orders; starting with an empty queue");
        }

        Task reader = Task.Run(() => ReadTelemetryAsync(stoppingToken), stoppingToken);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                RunPass();
                await Task.Delay(PassInterval, timeProvider, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The normal way out. Honour the token or the host hangs for its full shutdown
            // timeout on every deploy, and orchestrators eventually SIGKILL you mid-write.
        }
        finally
        {
            await reader.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false);
            logger.LogInformation(
                "WCS stopping. {Completed} completed, {Cancelled} cancelled, {Rejected} observations out of order",
                _completed,
                _cancelled,
                _rejectedOutOfOrder);
        }
    }

    /// <summary>
    /// Registers the fleet and learns the floor plan. Public so a test can drive the loop directly.
    /// </summary>
    public void Start()
    {
        logger.LogInformation("WCS starting against {Gateway}", gateway.Description);

        DateTimeOffset startedAt = timeProvider.GetUtcNow();

        foreach (EquipmentDescriptor machine in gateway.Fleet)
        {
            if (Equipment.Register(machine.Code, machine.Kind, startedAt).TryGetValue(out Equipment? equipment))
            {
                _fleet[machine.Id] = equipment;
            }
        }

        // Zone names come from the floor plan. A real deployment binds them from commissioning
        // configuration; the simulator publishes its own, so this asks it.
        _zones = gateway is Infrastructure.Automation.SimulatedPlcGateway simulator
            ? [.. simulator.Zones]
            : [.. Enumerable.Range(1, 12).Select(i => $"Z{i:00}")];

        logger.LogInformation("WCS ready: {Vehicles} vehicles across {Zones} zones", _fleet.Count, _zones.Length);
    }

    /// <summary>
    /// Takes back the orders that were open when this process last stopped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A pending order resumes; an assigned one is cancelled.</b> That asymmetry is the whole
    /// rule, and it is not pessimism. An order that was <see cref="TransportOrderStatus.Assigned"/>
    /// was being executed by a vehicle that has since stopped wherever it stopped — mid-aisle, at
    /// a station, or somewhere a person has already moved the pallet from. The database knows
    /// where the load unit was <i>going</i>; only the floor knows where it <i>is</i>. Resuming on
    /// the strength of the first is how a system confidently drives to collect a pallet that is
    /// not there.
    /// </para>
    /// <para>
    /// So the honest outcome is a cancellation, which is a visible fact somebody can act on, and a
    /// new order raised from the load unit's actual position once it is known. The same reasoning
    /// as <see cref="TransportOrder.Cancel"/>: cancelling is not an undo.
    /// </para>
    /// </remarks>
    /// <param name="open">Orders that had not finished, oldest first.</param>
    /// <param name="nowUtc">The current instant.</param>
    public void Restore(IReadOnlyList<TransportOrder> open, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(open);

        int resumed = 0, abandoned = 0;

        foreach (TransportOrder order in open)
        {
            if (order.Status == TransportOrderStatus.Pending)
            {
                _pending.Enqueue(order);
                resumed++;
                continue;
            }

            if (order.Cancel(nowUtc).IsSuccess)
            {
                MarkDirty(order);
                _cancelled++;
                abandoned++;
            }
        }

        logger.LogInformation(
            "Restored {Resumed} pending order(s); abandoned {Abandoned} that were in flight when the process stopped",
            resumed,
            abandoned);
    }

    /// <summary>Takes the orders that have changed since the last call, for the persister.</summary>
    public IReadOnlyCollection<TransportOrder> DrainDirty()
    {
        lock (_dirtyLock)
        {
            if (_dirty.Count == 0)
            {
                return [];
            }

            TransportOrder[] batch = [.. _dirty];
            _dirty.Clear();
            return batch;
        }
    }

    private void MarkDirty(TransportOrder order)
    {
        lock (_dirtyLock)
        {
            _dirty.Add(order);
        }
    }

    /// <summary>
    /// One pass of the loop. Public so a commissioning test can run a simulated hour without a host.
    /// </summary>
    /// <remarks>
    /// Exposing the pass rather than only the <see cref="BackgroundService"/> is what lets a test
    /// drive thousands of iterations against a <c>FakeTimeProvider</c> in milliseconds and get the
    /// same answer every time. A throughput assertion that depended on real delays would be a
    /// flaky test dressed up as a guarantee.
    /// </remarks>
    public void RunPass()
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        DrainRefusals(now);
        DrainTelemetry();
        RecoverFaults(now);
        TopUpWork(now);
        Dispatch(now);
        Heartbeat(now);
    }

    /// <summary>
    /// Says what the floor is doing, every few seconds.
    /// </summary>
    /// <remarks>
    /// Not decoration. A WCS that logs only exceptions is one you cannot tell apart from a WCS
    /// that has quietly stopped dispatching — which is the failure a supervisor is most likely to
    /// have, and the one nobody notices until a shift ends short. A periodic line saying how much
    /// work is moving is the cheapest possible liveness signal.
    /// </remarks>
    private void Heartbeat(DateTimeOffset now)
    {
        if (now - _lastHeartbeat < TimeSpan.FromSeconds(5))
        {
            return;
        }

        _lastHeartbeat = now;

        logger.LogInformation(
            "completed {Completed} · in flight {InFlight} · waiting for a route {Waiting} · pending {Pending} · zones held {Held}",
            _completed,
            _inFlight.Count,
            _awaitingRoute.Count,
            _pending.Count,
            _allocator.HeldZones.Count);
    }

    /// <summary>Queues a sample for the next pass. Called by the background reader and by tests.</summary>
    public void Enqueue(TelemetrySample sample) => _inbox.Enqueue(sample);

    private async Task ReadTelemetryAsync(CancellationToken ct)
    {
        try
        {
            await foreach (TelemetrySample sample in gateway.SubscribeAsync(ct).ConfigureAwait(false))
            {
                _inbox.Enqueue(sample);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown.
        }
        catch (Exception e)
        {
            // An unhandled exception in a BackgroundService kills the host by default — but this
            // task is not the one the host watches, so a failure here would otherwise stop
            // telemetry forever while the process kept running and looked healthy. That is the
            // worst of both outcomes, so it is logged loudly instead.
            logger.LogError(e, "Telemetry subscription failed; the WCS is now blind");
        }
    }

    /// <summary>
    /// Unwinds moves the machine would not take.
    /// </summary>
    /// <remarks>
    /// Without this, a refused command leaves the order assigned and its route allocated forever —
    /// and a leaked zone is permanent, because nothing releases it. That stretch of aisle is dead
    /// until the process restarts, and it will be blamed on the vehicles.
    /// </remarks>
    private void DrainRefusals(DateTimeOffset now)
    {
        while (_refused.TryDequeue(out EquipmentId id))
        {
            if (_inFlight.Remove(id, out TransportOrder? order))
            {
                // Cancelled rather than requeued. A machine refuses a move because it is faulted
                // or busy, so the load unit is not where the order assumed — the same reasoning as
                // RecoverFaults. A new order has to be raised from the pallet's actual position.
                order.Cancel(now);
                MarkDirty(order);
                _cancelled++;
            }

            GrantWaiting(_allocator.Release(id, now), now);
        }
    }

    private void DrainTelemetry()
    {
        while (_inbox.TryDequeue(out TelemetrySample sample))
        {
            if (!_fleet.TryGetValue(sample.EquipmentId, out Equipment? equipment))
            {
                continue;
            }

            if (!sample.IsTrustworthy)
            {
                // A value the device will not vouch for is not a state. Acting on it would let a
                // failed sensor drive a vehicle; ignoring it silently would hide the failure. So
                // it is dropped for control purposes and logged as the finding it is.
                logger.LogWarning(
                    "{Code} reported {Tag} with quality {Quality}; ignored for control",
                    equipment.Code,
                    sample.Tag,
                    sample.Quality);
                continue;
            }

            if (!string.Equals(sample.Tag, "state", StringComparison.Ordinal))
            {
                continue;
            }

            var reported = (EquipmentState)(int)sample.Value;
            EquipmentState before = equipment.State;

            Result applied = reported == EquipmentState.Faulted
                ? equipment.Fault("reported by machine", sample.SourceTimestampUtc)
                : equipment.ObserveState(reported, sample.SourceTimestampUtc);

            if (applied.IsFailure)
            {
                if (applied.Error.Code == "Automation.ObservationOutOfOrder")
                {
                    _rejectedOutOfOrder++;
                }

                continue;
            }

            OnStateChanged(equipment, sample.EquipmentId, before, sample.SourceTimestampUtc);
        }
    }

    private void OnStateChanged(Equipment equipment, EquipmentId id, EquipmentState before, DateTimeOffset atUtc)
    {
        if (equipment.State == EquipmentState.Faulted)
        {
            // The vehicle stopped where it faulted and is still occupying its route. Releasing the
            // zones here would be a lie with a physical consequence: another vehicle would be
            // routed through an aisle that is not empty. They stay held until somebody has
            // acknowledged, which is RecoverFaults below.
            logger.LogWarning("{Code} faulted; its route stays allocated until acknowledged", equipment.Code);
            return;
        }

        bool arrived = before == EquipmentState.Running && equipment.State == EquipmentState.Idle;
        if (!arrived || !_inFlight.Remove(id, out TransportOrder? order))
        {
            return;
        }

        if (order.Complete(atUtc).IsSuccess)
        {
            MarkDirty(order);
            _completed++;
        }

        GrantWaiting(_allocator.Release(id, atUtc), atUtc);
    }

    private void RecoverFaults(DateTimeOffset now)
    {
        foreach ((EquipmentId id, Equipment equipment) in _fleet)
        {
            if (!equipment.HasUnacknowledgedFault || now - equipment.StateChangedAtUtc < TechnicianResponse)
            {
                continue;
            }

            if (equipment.Acknowledge("maintenance", now).IsFailure)
            {
                continue;
            }

            // The load unit is wherever the vehicle stopped, which is not where the order said it
            // would be. Cancelling is the honest outcome: a new order has to be raised from the
            // pallet's ACTUAL position, and inventing a completion would produce a pallet the
            // system cannot find. See TransportOrder.Cancel for why this is not an undo.
            if (_inFlight.Remove(id, out TransportOrder? interrupted)
                || _awaitingRoute.Remove(id, out interrupted))
            {
                interrupted.Cancel(now);
                MarkDirty(interrupted);
                _cancelled++;
            }

            _ = AcknowledgeAsync(id, equipment);
            GrantWaiting(_allocator.Release(id, now), now);
        }
    }

    private void TopUpWork(DateTimeOffset now)
    {
        // A real WCS is fed by a WMS. This invents work so the demonstration has something to do,
        // and it is seeded so two runs of the commissioning test are the same run.
        int attempts = 0;
        while (_pending.Count < _fleet.Count * 2 && attempts++ < 20)
        {
            string from = _zones[_random.Next(_zones.Length)];
            string to = _zones[_random.Next(_zones.Length)];

            // Create refuses a same-zone route, which the dice will produce often on a small
            // floor. Not an error; just nothing to add this time round.
            if (TransportOrder.Create($"UDC-{_random.Next(100_000, 999_999)}", from, to, now)
                .TryGetValue(out TransportOrder? order))
            {
                _pending.Enqueue(order);
                MarkDirty(order);
            }
        }
    }

    private void Dispatch(DateTimeOffset now)
    {
        foreach ((EquipmentId id, Equipment equipment) in _fleet)
        {
            if (_pending.Count == 0)
            {
                return;
            }

            // Busy, already holding a route, or waiting in the allocator's queue for one.
            if (_inFlight.ContainsKey(id) || _awaitingRoute.ContainsKey(id) || !equipment.CanAcceptWork)
            {
                continue;
            }

            TransportOrder order = _pending.Dequeue();

            // All-or-nothing over the whole route. A vehicle never holds part of one, which is
            // what makes deadlock impossible here rather than merely unlikely: a cycle in a
            // wait-for graph needs two parties each holding something the other wants.
            Allocation allocation = _allocator.Request(id, [order.FromZone, order.ToZone], now);

            if (allocation.IsGranted)
            {
                Begin(id, equipment, order, now);
                continue;
            }

            // Queued behind somebody. The order belongs to this vehicle now and will start the
            // moment the allocator grants it — see GrantWaiting. Putting it back on the pending
            // queue instead would lose its place, which is precisely how starvation begins.
            _awaitingRoute[id] = order;
        }
    }

    /// <summary>Starts the vehicles whose queued routes were granted by a release.</summary>
    private void GrantWaiting(IReadOnlyList<EquipmentId> served, DateTimeOffset now)
    {
        foreach (EquipmentId id in served)
        {
            if (!_awaitingRoute.Remove(id, out TransportOrder? order) || !_fleet.TryGetValue(id, out Equipment? equipment))
            {
                continue;
            }

            Begin(id, equipment, order, now);
        }
    }

    private void Begin(EquipmentId id, Equipment equipment, TransportOrder order, DateTimeOffset now)
    {
        if (order.AssignTo(equipment, now).IsFailure)
        {
            _allocator.Release(id, now);
            _pending.Enqueue(order);
            return;
        }

        _inFlight[id] = order;
        MarkDirty(order);

        // Fire and forget is correct here and nowhere else: the acknowledgement means "I have the
        // instruction", and the outcome arrives on the telemetry stream. Awaiting it would block
        // the one loop the entire system runs on, behind a network call to a machine.
        _ = SendMoveAsync(id, equipment, order);
    }

    private async Task SendMoveAsync(EquipmentId id, Equipment equipment, TransportOrder order)
    {
        try
        {
            CommandAck ack = await gateway
                .SendAsync(new EquipmentCommand(id, "move", order.ToZone), CancellationToken.None)
                .ConfigureAwait(false);

            if (!ack.Accepted)
            {
                logger.LogWarning("{Code} refused the move: {Detail}", equipment.Code, ack.Detail);
                _refused.Enqueue(id);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Sending a move to {Code} threw", equipment.Code);
            _refused.Enqueue(id);
        }
    }

    private async Task AcknowledgeAsync(EquipmentId id, Equipment equipment)
    {
        try
        {
            CommandAck ack = await gateway
                .AcknowledgeAsync(id, "maintenance", CancellationToken.None)
                .ConfigureAwait(false);

            if (!ack.Accepted)
            {
                logger.LogWarning("{Code} would not accept the acknowledgement: {Detail}", equipment.Code, ack.Detail);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Acknowledging {Code} threw", equipment.Code);
        }
    }
}
