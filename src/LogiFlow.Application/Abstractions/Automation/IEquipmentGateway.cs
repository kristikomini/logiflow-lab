using LogiFlow.Domain.Automation;

namespace LogiFlow.Application.Abstractions.Automation;

/// <summary>One machine this gateway serves.</summary>
/// <param name="Id">Its identifier in our system.</param>
/// <param name="Code">The identifier painted on its cabinet, such as <c>LGV-04</c>.</param>
/// <param name="Kind">What kind of machine it is.</param>
/// <remarks>
/// <b>Why a gateway is told its fleet rather than discovering it.</b> On a real installation the
/// equipment list is commissioning data — somebody wrote down that <c>CNV-12</c> is at that
/// address with that register map — and Modbus has no request that means "what do you have".
/// OPC UA does, and an OPC UA adapter could populate this by browsing its address space; the
/// interface is the same either way, which is the point of having one.
/// </remarks>
public readonly record struct EquipmentDescriptor(EquipmentId Id, string Code, EquipmentKind Kind);

/// <summary>Something to ask a machine to do.</summary>
/// <param name="EquipmentId">Which machine.</param>
/// <param name="Verb">What to do, in the machine layer's vocabulary — <c>move</c>, <c>stop</c>, <c>home</c>.</param>
/// <param name="Argument">The parameter, when the verb takes one — usually a destination zone.</param>
public readonly record struct EquipmentCommand(EquipmentId EquipmentId, string Verb, string? Argument);

/// <summary>
/// The machine's answer to a command. Not the outcome — see <see cref="IEquipmentGateway.SendAsync"/>.
/// </summary>
/// <param name="Accepted">Whether the machine took the instruction.</param>
/// <param name="Detail">Why not, when it did not. Empty on success.</param>
public readonly record struct CommandAck(bool Accepted, string Detail)
{
    /// <summary>The machine took it.</summary>
    public static CommandAck Ok() => new(true, string.Empty);

    /// <summary>The machine refused it, and said why.</summary>
    public static CommandAck Refused(string detail) => new(false, detail);
}

/// <summary>
/// The boundary between this software and the machines. One port, several adapters, and the
/// only place in the solution allowed to know what a register or a node id is.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists at all.</b> The machine layer is the one dependency in this repository that
/// genuinely cannot be run on a laptop — there is no LocalDB for a filling line. Without a port
/// there is no test, no demo, no development away from site, and nothing a new colleague can run
/// on their first day. The alternative is a codebase whose only test environment is a customer's
/// factory during a four-hour Sunday window.
/// </para>
/// <para>
/// It is the same move this repository already makes with <c>IDistributedCache</c> — see the root
/// <c>README.md</c> — and it carries the same honesty requirement. The in-memory cache is not
/// distributed and that difference bites when you scale out; the simulator is not a machine and
/// that difference bites at commissioning. Both are worth it; neither should be claimed to be
/// something it is not.
/// </para>
/// <para>
/// <b>The interface speaks the machine's vocabulary, not the wire's.</b> There is no
/// <c>ReadRegister(int)</c> here. That would not be an abstraction, it would be Modbus with C#
/// syntax, and the day a customer's next line speaks OPC UA you would find "register number" had
/// leaked into three hundred call sites.
/// </para>
/// <para>
/// <b>Where the abstraction leaks, and it does.</b> OPC UA carries a status code with dozens of
/// values; Modbus carries none at all. <see cref="Quality"/> is therefore a lossy projection of
/// one and an outright invention for the other, and one day somebody will need the specific code.
/// That is a real cost, stated rather than hidden.
/// </para>
/// Covered in: <c>course/module-28-industrial-and-ot/01-the-boundary.md</c>
/// </remarks>
public interface IEquipmentGateway
{
    /// <summary>
    /// A human-readable name for whichever adapter is registered — <c>simulated PLC</c>,
    /// <c>Modbus TCP 10.4.1.20:502</c>. Logged at startup so nobody has to guess which one is live.
    /// </summary>
    string Description { get; }

    /// <summary>The machines this gateway serves. Fixed for the lifetime of the gateway.</summary>
    IReadOnlyList<EquipmentDescriptor> Fleet { get; }

    /// <summary>
    /// Everything the equipment reports, as it changes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>There is deliberately no <c>GetCurrentState()</c> on this interface.</b> Offering one is
    /// an invitation to poll, and polling loses events: a fault that latches and clears in 60 ms
    /// is invisible at any interval you can afford, and the events that matter most on a line are
    /// the shortest. Polling also costs the <i>device</i> rather than you — asking 5,000 tags
    /// every second is real work for a PLC with a scan cycle to meet, which is how a supervisor
    /// sitting quietly at a desk degrades a production line.
    /// </para>
    /// <para>
    /// A caller that needs "the value right now" keeps a projection of this stream in memory. That
    /// is a dictionary, not a round trip to the floor.
    /// </para>
    /// </remarks>
    /// <param name="ct">Cancellation. Honoured promptly, or the host hangs on every deploy.</param>
    IAsyncEnumerable<TelemetrySample> SubscribeAsync(CancellationToken ct);

    /// <summary>
    /// Asks a machine to do something, and returns when it has been <b>accepted</b>.
    /// </summary>
    /// <remarks>
    /// <b>Not when it is done.</b> A conveyor does not finish moving a pallet inside your
    /// <c>await</c>; the outcome arrives later on <see cref="SubscribeAsync"/>, as an observation
    /// like everything else. Modelling this as <c>Task&lt;MoveResult&gt;</c> is the mistake that
    /// produces a supervisor holding a request open for ninety seconds, and that falls over the
    /// first time a pallet jams.
    /// </remarks>
    /// <param name="command">What to ask for.</param>
    /// <param name="ct">Cancellation.</param>
    Task<CommandAck> SendAsync(EquipmentCommand command, CancellationToken ct);

    /// <summary>
    /// Clears a latched fault on the machine, on a named person's authority.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="SendAsync"/> because it is separately audited: it is the one
    /// operation an auditor asks about — who cleared the fault, when, and on whose authority.
    /// Giving it its own seam is what makes the audit trail write itself.
    /// </remarks>
    /// <param name="equipmentId">The machine.</param>
    /// <param name="acknowledgedBy">Who is clearing it.</param>
    /// <param name="ct">Cancellation.</param>
    Task<CommandAck> AcknowledgeAsync(EquipmentId equipmentId, string acknowledgedBy, CancellationToken ct);
}
