namespace LogiFlow.Domain.Automation;

/// <summary>What happened to a request for a route.</summary>
public enum AllocationOutcome
{
    /// <summary>The vehicle now holds every zone on the route and may move.</summary>
    Granted = 0,

    /// <summary>Another vehicle holds part of the route. The request is queued, in arrival order.</summary>
    Queued = 1,

    /// <summary>This vehicle already holds zones. It must release before asking again.</summary>
    AlreadyHolding = 2,
}

/// <summary>The result of asking for a route, and the queue position when it could not be granted.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="QueuePosition">Zero-based position in the waiting queue, or <c>null</c> when granted.</param>
public readonly record struct Allocation(AllocationOutcome Outcome, int? QueuePosition)
{
    /// <summary>True when the vehicle may move.</summary>
    public bool IsGranted => Outcome == AllocationOutcome.Granted;
}

/// <summary>One line of the allocator's journal.</summary>
/// <param name="AtUtc">When it happened.</param>
/// <param name="Vehicle">Who asked or released.</param>
/// <param name="Action">What happened, in the allocator's own words.</param>
/// <param name="Zones">The zones involved.</param>
public readonly record struct AllocationJournalEntry(
    DateTimeOffset AtUtc,
    EquipmentId Vehicle,
    string Action,
    IReadOnlyList<string> Zones);

/// <summary>
/// The traffic manager: decides which vehicle may occupy which stretch of floor.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deadlock is prevented here by construction, and the construction is one rule: a vehicle
/// never holds part of a route.</b> Every request is all-or-nothing over the whole route. A
/// cycle in a wait-for graph needs two parties each holding something the other wants, and a
/// party that holds nothing until it holds everything can never be one of them. That is a
/// stronger guarantee than acquiring in a sorted order, and it is why this class has no
/// ordering rule in it at all.
/// </para>
/// <para>
/// The reason it must be prevented rather than detected: <b>nothing times out on a factory
/// floor.</b> Two vehicles nose to nose in an aisle are still there in the morning, there is no
/// user who gives up and refreshes, and reversing a loaded vehicle back down a route it planned
/// forwards is a physical manoeuvre with its own safety case rather than a <c>catch</c> block.
/// </para>
/// <para>
/// <b>All-or-nothing on its own trades deadlock for starvation</b>, which is worse in one
/// specific way: deadlock is obvious and starvation is not. An unlucky vehicle asks, is refused,
/// retries, and is overtaken by vehicles with shorter routes — forever — while the dashboard
/// shows healthy throughput and nobody raises a ticket. The queue below is the fix: requests wait
/// in arrival order, and a later request may not overtake a waiting one by taking a zone that
/// waiting one needs. It costs a little throughput, and that cost is exactly why somebody
/// optimising without knowing what it was for will eventually delete it.
/// </para>
/// <para>
/// <b>Why this is one single-threaded object rather than a distributed lock per zone.</b> You are
/// granting tens of routes per second, not millions. One component that owns the whole zone table
/// and processes requests in order is trivially fair, trivially auditable, and fast enough by
/// several orders of magnitude. Reaching for distributed locking here is a self-inflicted wound.
/// It is <b>not</b> thread-safe on purpose: callers serialise access, which is what the WCS
/// dispatcher's single loop does.
/// </para>
/// <para>
/// <b>Zone names are compared case-insensitively</b> because they come from a floor plan drawn by
/// a person, and <c>AISLE-3</c> and <c>Aisle-3</c> being two different zones is a collision
/// waiting to happen.
/// </para>
/// Covered in: <c>course/module-28-industrial-and-ot/04-traffic-and-deadlock.md</c>
/// </remarks>
public sealed class ZoneAllocator
{
    private readonly Dictionary<string, EquipmentId> _heldBy = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PendingRequest> _queue = [];
    private readonly List<AllocationJournalEntry> _journal = [];

    /// <summary>
    /// Every grant, denial and release, in order.
    /// </summary>
    /// <remarks>
    /// When a line stops, the question is <i>why did vehicle 7 wait four minutes</i>, and this is
    /// the only thing that can answer it. In a long-running process this list would be written to
    /// a log sink and trimmed rather than grown forever; it is kept in memory here because the
    /// commissioning tests assert against it.
    /// </remarks>
    public IReadOnlyList<AllocationJournalEntry> Journal => _journal;

    /// <summary>Zones currently held, and by whom.</summary>
    public IReadOnlyDictionary<string, EquipmentId> HeldZones => _heldBy;

    /// <summary>How many requests are waiting.</summary>
    public int QueueLength => _queue.Count;

    /// <summary>
    /// Asks for every zone on a route at once.
    /// </summary>
    /// <param name="vehicle">Who is asking.</param>
    /// <param name="route">The zones the vehicle will occupy. Duplicates are ignored.</param>
    /// <param name="nowUtc">The current instant, for the journal.</param>
    public Allocation Request(EquipmentId vehicle, IEnumerable<string> route, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(route);

        string[] zones = [.. route.Distinct(StringComparer.OrdinalIgnoreCase)];

        // Asking while still holding is a caller bug, not a traffic condition: it is the one way
        // back to hold-and-wait, and therefore the one way back to deadlock. Refused loudly.
        if (_heldBy.Values.Contains(vehicle))
        {
            Record(nowUtc, vehicle, "refused — already holding", zones);
            return new Allocation(AllocationOutcome.AlreadyHolding, null);
        }

        if (_queue.Exists(r => r.Vehicle.Equals(vehicle)))
        {
            return new Allocation(AllocationOutcome.Queued, _queue.FindIndex(r => r.Vehicle.Equals(vehicle)));
        }

        if (CanGrant(zones, skipQueueCheck: false))
        {
            Grant(vehicle, zones, nowUtc);
            return new Allocation(AllocationOutcome.Granted, null);
        }

        _queue.Add(new PendingRequest(vehicle, zones, nowUtc));
        Record(nowUtc, vehicle, "queued", zones);
        return new Allocation(AllocationOutcome.Queued, _queue.Count - 1);
    }

    /// <summary>
    /// Gives back everything a vehicle holds, then serves whoever the queue allows.
    /// </summary>
    /// <remarks>
    /// Safe to call for a vehicle that holds nothing — a dispatcher unwinding after a fault does
    /// not always know what was granted, and making it find out first would be an invitation to
    /// leak zones on the error path. Leaked zones are permanent: nothing releases them, and that
    /// stretch of aisle is dead until the process restarts.
    /// </remarks>
    /// <param name="vehicle">Who is releasing.</param>
    /// <param name="nowUtc">The current instant, for the journal.</param>
    /// <returns>The vehicles whose queued requests were granted as a result.</returns>
    public IReadOnlyList<EquipmentId> Release(EquipmentId vehicle, DateTimeOffset nowUtc)
    {
        string[] released = [.. _heldBy.Where(pair => pair.Value.Equals(vehicle)).Select(pair => pair.Key)];

        foreach (string zone in released)
        {
            _heldBy.Remove(zone);
        }

        if (released.Length > 0)
        {
            Record(nowUtc, vehicle, "released", released);
        }

        return DrainQueue(nowUtc);
    }

    /// <summary>
    /// Rebuilds the allocator's picture from where vehicles physically are.
    /// </summary>
    /// <remarks>
    /// <b>This is the restart path, and it is a safety matter rather than an availability one.</b>
    /// After a crash the vehicles are wherever they are, holding real space, whatever this process
    /// believes. An allocator that comes back with an empty table and starts granting will put two
    /// vehicles in one aisle while the software is convinced everything is fine. So state is
    /// rebuilt from what the machines report about themselves — never from anything this process
    /// wrote before it died.
    /// </remarks>
    /// <param name="occupancy">Zone to occupying vehicle, as reported by the floor.</param>
    /// <param name="nowUtc">The current instant, for the journal.</param>
    public void RebuildFromFloor(IEnumerable<KeyValuePair<string, EquipmentId>> occupancy, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(occupancy);

        _heldBy.Clear();
        _queue.Clear();

        foreach ((string zone, EquipmentId vehicle) in occupancy)
        {
            _heldBy[zone] = vehicle;
            Record(nowUtc, vehicle, "rebuilt from floor", [zone]);
        }
    }

    /// <summary>
    /// Whether a route is free — and, unless we are serving the queue itself, whether taking it
    /// would overtake somebody already waiting.
    /// </summary>
    private bool CanGrant(string[] zones, bool skipQueueCheck)
    {
        foreach (string zone in zones)
        {
            if (_heldBy.ContainsKey(zone))
            {
                return false;
            }
        }

        if (skipQueueCheck)
        {
            return true;
        }

        // The anti-starvation rule. A newcomer whose route is entirely free may still not have
        // it, if any of those zones is one a waiting vehicle is holding out for. Without this,
        // short routes stream past a long one indefinitely and the throughput graph looks fine.
        foreach (PendingRequest waiting in _queue)
        {
            foreach (string zone in zones)
            {
                if (waiting.Zones.Contains(zone, StringComparer.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private List<EquipmentId> DrainQueue(DateTimeOffset nowUtc)
    {
        List<EquipmentId> served = [];

        // Front to back, and it does not stop at the first refusal: a request further down whose
        // zones are free and do not belong to anybody ahead of it may proceed. Stopping at the
        // head would be strict FIFO, which is fair and needlessly slow — one vehicle waiting for
        // a busy aisle would hold up every unrelated move in the building.
        for (int i = 0; i < _queue.Count;)
        {
            PendingRequest request = _queue[i];

            bool blockedByEarlier = false;
            for (int earlier = 0; earlier < i && !blockedByEarlier; earlier++)
            {
                foreach (string zone in request.Zones)
                {
                    if (_queue[earlier].Zones.Contains(zone, StringComparer.OrdinalIgnoreCase))
                    {
                        blockedByEarlier = true;
                        break;
                    }
                }
            }

            if (!blockedByEarlier && CanGrant(request.Zones, skipQueueCheck: true))
            {
                _queue.RemoveAt(i);
                Grant(request.Vehicle, request.Zones, nowUtc);
                served.Add(request.Vehicle);
                continue;       // do not advance: the list shifted under us
            }

            i++;
        }

        return served;
    }

    private void Grant(EquipmentId vehicle, string[] zones, DateTimeOffset nowUtc)
    {
        foreach (string zone in zones)
        {
            _heldBy[zone] = vehicle;
        }

        Record(nowUtc, vehicle, "granted", zones);
    }

    private void Record(DateTimeOffset atUtc, EquipmentId vehicle, string action, IReadOnlyList<string> zones) =>
        _journal.Add(new AllocationJournalEntry(atUtc, vehicle, action, zones));

    private sealed record PendingRequest(EquipmentId Vehicle, string[] Zones, DateTimeOffset RequestedAtUtc);
}
