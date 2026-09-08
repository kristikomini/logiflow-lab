using LogiFlow.Domain.Automation.Events;
using LogiFlow.Domain.Common;
using LogiFlow.Domain.Results;

namespace LogiFlow.Domain.Automation;

/// <summary>What kind of machine this is. Decides what it can be asked to do, not how it behaves.</summary>
public enum EquipmentKind
{
    /// <summary>A powered belt or roller section that moves load units along one path.</summary>
    Conveyor = 0,

    /// <summary>A junction that sends a load unit down one of several paths.</summary>
    Diverter = 1,

    /// <summary>An aisle-serving crane in an automated warehouse — a <i>trasloelevatore</i>.</summary>
    StackerCrane = 2,

    /// <summary>An automated guided vehicle. Free-roaming, and therefore the one that can deadlock.</summary>
    Agv = 3,
}

/// <summary>
/// The state a machine reports it is in. Five values, and the difference between three of them
/// is the whole of a maintenance conversation.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="Starved"/> and <see cref="Blocked"/> are not faults</b>, and separating them
/// from <see cref="Faulted"/> is what turns downtime reporting from a scoreboard into something
/// actionable. Starved means nothing arrived from upstream; blocked means downstream will not
/// take anything. In both cases this machine is working perfectly and is being punished for
/// somebody else's problem — and if you record all three as "stopped", the best machine on the
/// line gets the worst number and nobody can explain why.
/// </para>
/// Covered in: <c>course/module-28-industrial-and-ot/05-oee-and-traceability.md</c>
/// </remarks>
public enum EquipmentState
{
    /// <summary>Powered, healthy, nothing to do.</summary>
    Idle = 0,

    /// <summary>Doing work.</summary>
    Running = 1,

    /// <summary>Cannot pass work downstream. Not a fault — downstream's problem.</summary>
    Blocked = 2,

    /// <summary>Nothing arriving from upstream. Not a fault — upstream's problem.</summary>
    Starved = 3,

    /// <summary>A latched fault. Requires acknowledgement before it can run again.</summary>
    Faulted = 4,
}

/// <summary>
/// One physically identifiable machine on the floor, and the state machine that governs it.
/// Aggregate root.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this aggregate is for.</b> It is not a mirror of the PLC — the PLC owns reality, and
/// this object will always be a few milliseconds behind it. It is the place where the <i>rules
/// about transitions</i> live, so that an illegal one is a rejected command with a reason rather
/// than a command silently sent to a machine that cannot honour it.
/// </para>
/// <para>
/// <b>The transition that matters is <see cref="EquipmentState.Faulted"/> → anything.</b> A fault
/// is <i>latched</i>: it stays until a human acknowledges it, precisely so that a machine which
/// failed for a reason nobody looked at cannot quietly resume. Software that clears a fault by
/// sending a start command is software that has removed a safety-adjacent human checkpoint, and
/// on a real line that is the bug that costs money — or worse.
/// </para>
/// <para>
/// <b>Why the state comes in rather than being decided here.</b> <see cref="ObserveState"/> takes
/// what the machine reports. We do not get to disagree with a conveyor about whether it is
/// running. What we <i>do</i> get to do is refuse to <b>ask</b> for something impossible, which is
/// <see cref="CanAcceptWork"/> and <see cref="Acknowledge"/>.
/// </para>
/// Covered in: <c>course/module-28-industrial-and-ot/01-the-boundary.md</c>
/// </remarks>
public sealed class Equipment : AggregateRoot<EquipmentId>
{
    private Equipment(EquipmentId id, string code, EquipmentKind kind, DateTimeOffset createdAtUtc) : base(id)
    {
        Code = code;
        Kind = kind;
        State = EquipmentState.Idle;
        StateChangedAtUtc = createdAtUtc;
    }

    private Equipment()
    {
    }

    /// <summary>
    /// The identifier painted on the cabinet, such as <c>CNV-12</c> or <c>LGV-04</c>.
    /// </summary>
    /// <remarks>
    /// This, not the <see cref="EquipmentId"/>, is what an electrician says on the phone at
    /// 03:00. A GUID nobody can read is the right primary key and the wrong thing to log.
    /// </remarks>
    public string Code { get; private set; } = null!;

    /// <summary>What kind of machine it is.</summary>
    public EquipmentKind Kind { get; private set; }

    /// <summary>The last state the machine reported.</summary>
    public EquipmentState State { get; private set; }

    /// <summary>
    /// When the machine says it entered <see cref="State"/> — source time, not receive time.
    /// </summary>
    /// <remarks>
    /// Availability in an OEE calculation is the sum of the intervals between these instants, so
    /// using the receive time here would silently import network jitter into a number somebody
    /// reports to a plant manager.
    /// </remarks>
    public DateTimeOffset StateChangedAtUtc { get; private set; }

    /// <summary>
    /// When the machine last told us anything, or <c>null</c> if it never has.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Distinct from <see cref="StateChangedAtUtc"/>, and the distinction matters twice. First, a
    /// machine that has been <see cref="EquipmentState.Running"/> for an hour has an old state
    /// change and a recent observation, and only the second tells you the link is alive — a
    /// gateway that died silently looks exactly like a machine that is behaving.
    /// </para>
    /// <para>
    /// Second, it is what makes the out-of-order guard in <see cref="ObserveState"/> correct at
    /// startup. Registration is not an observation, so a first sample carrying a source timestamp
    /// slightly earlier than the moment we registered the machine — which is normal, because a
    /// reading is already a scan cycle old when it reaches us — must be accepted rather than
    /// rejected as stale.
    /// </para>
    /// </remarks>
    public DateTimeOffset? LastObservedAtUtc { get; private set; }

    /// <summary>The reason for the current fault, or <c>null</c> when not faulted.</summary>
    public string? FaultReason { get; private set; }

    /// <summary>True while a fault is latched and unacknowledged.</summary>
    public bool HasUnacknowledgedFault => FaultReason is not null;

    /// <summary>
    /// Whether it is sensible to dispatch work to this machine right now.
    /// </summary>
    /// <remarks>
    /// <see cref="EquipmentState.Blocked"/> and <see cref="EquipmentState.Starved"/> are
    /// deliberately excluded even though neither is a fault: the machine is healthy but cannot
    /// take work, and queueing more onto it makes the congestion worse rather than better.
    /// </remarks>
    public bool CanAcceptWork => State is EquipmentState.Idle or EquipmentState.Running
                                 && !HasUnacknowledgedFault;

    /// <summary>Registers a machine that exists on the floor.</summary>
    /// <param name="code">The identifier painted on the cabinet.</param>
    /// <param name="kind">What kind of machine it is.</param>
    /// <param name="nowUtc">
    /// The current instant, passed in rather than read from the clock — see
    /// <c>course/module-23-text-culture-serialization</c> for why a domain object never calls
    /// <c>DateTimeOffset.UtcNow</c> itself.
    /// </param>
    public static Result<Equipment> Register(string code, EquipmentKind kind, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return AutomationErrors.EquipmentCodeRequired;
        }

        return new Equipment(EquipmentId.New(), code.Trim(), kind, nowUtc);
    }

    /// <summary>
    /// Records what the machine reports it is doing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Returns <see cref="Result.Success()"/> for a repeat of the current state and does nothing:
    /// a subscription delivers changes, but a reconnecting gateway re-sends the current value, and
    /// treating that as a transition would produce a stop event of zero length every time the
    /// network hiccuped.
    /// </para>
    /// <para>
    /// The one transition it refuses is leaving <see cref="EquipmentState.Faulted"/> while the
    /// fault is unacknowledged. If a machine claims to be running with a latched fault, something
    /// is wrong with our picture of it rather than with the machine, and inventing a clean
    /// transition would erase the evidence.
    /// </para>
    /// </remarks>
    /// <param name="state">The reported state.</param>
    /// <param name="sourceTimestampUtc">When the machine says it entered that state.</param>
    public Result ObserveState(EquipmentState state, DateTimeOffset sourceTimestampUtc)
    {
        // Out-of-order delivery is normal after a gateway reconnects and flushes its buffer.
        // Applying an older observation would rewind the machine, so it is ignored — but the
        // sample itself is still stored by the caller, because the gap it reveals is a finding.
        // Compared against the last OBSERVATION rather than the last state change: registration
        // is not an observation, and a first reading is legitimately a scan cycle old.
        if (LastObservedAtUtc is { } last && sourceTimestampUtc < last)
        {
            return AutomationErrors.ObservationOutOfOrder(Code);
        }

        LastObservedAtUtc = sourceTimestampUtc;

        if (state == State)
        {
            return Result.Success();
        }

        if (State == EquipmentState.Faulted && HasUnacknowledgedFault)
        {
            return AutomationErrors.FaultNotAcknowledged(Code);
        }

        EquipmentState previous = State;
        State = state;
        StateChangedAtUtc = sourceTimestampUtc;

        Raise(new EquipmentStateChangedDomainEvent(Id, Code, previous, state, sourceTimestampUtc));
        return Result.Success();
    }

    /// <summary>Latches a fault. Idempotent: a repeat of the same fault is not a new event.</summary>
    /// <param name="reason">What the machine said. Stored verbatim; it is evidence.</param>
    /// <param name="sourceTimestampUtc">When the machine says it faulted.</param>
    public Result Fault(string reason, DateTimeOffset sourceTimestampUtc)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return AutomationErrors.FaultReasonRequired;
        }

        if (State == EquipmentState.Faulted && FaultReason == reason)
        {
            return Result.Success();
        }

        EquipmentState previous = State;
        State = EquipmentState.Faulted;
        FaultReason = reason;
        StateChangedAtUtc = sourceTimestampUtc;
        LastObservedAtUtc = sourceTimestampUtc;

        Raise(new EquipmentFaultedDomainEvent(Id, Code, reason, sourceTimestampUtc));

        if (previous != EquipmentState.Faulted)
        {
            Raise(new EquipmentStateChangedDomainEvent(Id, Code, previous, EquipmentState.Faulted, sourceTimestampUtc));
        }

        return Result.Success();
    }

    /// <summary>
    /// Clears a latched fault, on a named person's authority.
    /// </summary>
    /// <remarks>
    /// <b>This is the audited operation.</b> It exists as its own method rather than as a flag on
    /// a general-purpose command precisely because it is the one an auditor asks about: who
    /// cleared the fault, when, and on whose authority. Note that it does <i>not</i> start the
    /// machine — acknowledging is permission to run again, not a start command, and conflating
    /// the two removes a human checkpoint that exists on purpose.
    /// </remarks>
    /// <param name="acknowledgedBy">Who cleared it. Required — "system" is not an answer here.</param>
    /// <param name="nowUtc">The current instant.</param>
    public Result Acknowledge(string acknowledgedBy, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(acknowledgedBy))
        {
            return AutomationErrors.AcknowledgerRequired;
        }

        if (!HasUnacknowledgedFault)
        {
            return AutomationErrors.NothingToAcknowledge(Code);
        }

        string cleared = FaultReason!;
        FaultReason = null;

        Raise(new EquipmentFaultAcknowledgedDomainEvent(Id, Code, cleared, acknowledgedBy.Trim(), nowUtc));
        return Result.Success();
    }
}
