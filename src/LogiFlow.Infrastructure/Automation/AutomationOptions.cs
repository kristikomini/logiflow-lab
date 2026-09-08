using System.ComponentModel.DataAnnotations;

namespace LogiFlow.Infrastructure.Automation;

/// <summary>
/// Everything the machine layer reads from configuration.
/// </summary>
/// <remarks>
/// <para>
/// <b>The default is the simulator, and that is the same decision the mailer makes.</b> A missing
/// configuration section must not produce a system that starts sending commands to real machinery
/// — so a clone-and-run gets a simulated warehouse, visibly labelled as one, and reaching a real
/// PLC requires somebody to have typed a host name on purpose.
/// </para>
/// <para>
/// The selection is a <b>capability check</b>, not an environment check: <c>Modbus:Host</c> is
/// either configured or it is not. Asking "is this Production?" instead is the mistake that gives
/// you a staging environment quietly driving a customer's conveyors, and the same reasoning is
/// spelled out for the OTLP exporter in the API's <c>Program.cs</c>.
/// </para>
/// Covered in: <c>course/module-28-industrial-and-ot/01-the-boundary.md</c>
/// </remarks>
public sealed class AutomationOptions
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "Automation";

    /// <summary>Modbus TCP connection details, or <c>null</c> to run the simulator.</summary>
    public ModbusOptions? Modbus { get; init; }

    /// <summary>Settings for the in-process simulated warehouse.</summary>
    public SimulationOptions Simulation { get; init; } = new();
}

/// <summary>Where the real equipment is.</summary>
public sealed class ModbusOptions
{
    /// <summary>Host name or address of the PLC or gateway.</summary>
    [Required]
    public string Host { get; init; } = string.Empty;

    /// <summary>
    /// TCP port. 502 is the registered Modbus port and the overwhelmingly common answer.
    /// </summary>
    [Range(1, 65535)]
    public int Port { get; init; } = 502;

    /// <summary>
    /// The unit (slave) id. Meaningful when several devices sit behind one serial gateway.
    /// </summary>
    [Range(0, 255)]
    public int UnitId { get; init; } = 1;

    /// <summary>
    /// How often to read the device.
    /// </summary>
    /// <remarks>
    /// <b>Yes, this adapter polls, and that is a property of Modbus rather than a design choice.</b>
    /// The protocol has no subscription: there is no request meaning "tell me when this changes".
    /// So the adapter polls at this interval and turns the differences into the change stream the
    /// port promises — which means it inherits every weakness of polling, and a transient shorter
    /// than this period is simply never seen. That is a reason to prefer OPC UA where the
    /// installation offers it, not a reason to set this to 10 ms and hope.
    /// </remarks>
    [Range(20, 60_000)]
    public int PollMilliseconds { get; init; } = 250;

    /// <summary>How long to wait for a device before giving up on a read.</summary>
    [Range(100, 60_000)]
    public int TimeoutMilliseconds { get; init; } = 2_000;
}

/// <summary>Shape of the simulated warehouse.</summary>
public sealed class SimulationOptions
{
    /// <summary>How many vehicles exist.</summary>
    [Range(1, 200)]
    public int Vehicles { get; init; } = 4;

    /// <summary>How many zones the floor is divided into.</summary>
    /// <remarks>
    /// <b>This is the throughput dial.</b> Coarse zones serialise moves that never actually
    /// conflict — two vehicles at opposite ends of a long aisle are not in each other's way, but
    /// if the aisle is one zone then one of them waits. Cut too finely and the vehicles spend
    /// their time asking permission, and a zone smaller than a vehicle plus its braking distance
    /// is not a zone at all. Watching this number change the throughput is most of what
    /// "throughput optimisation" means in an intralogistics job advert.
    /// </remarks>
    [Range(2, 500)]
    public int Zones { get; init; } = 12;

    /// <summary>How long a vehicle takes to traverse a zone, in milliseconds of simulated time.</summary>
    [Range(1, 600_000)]
    public int TravelMilliseconds { get; init; } = 400;

    /// <summary>
    /// Seed for the simulator's randomness, so a run is reproducible.
    /// </summary>
    /// <remarks>
    /// The whole value of a simulator is running the same scenario a thousand times. An unseeded
    /// <c>Random</c> turns a failing commissioning test into a story about a bad afternoon.
    /// </remarks>
    public int Seed { get; init; } = 20260908;

    /// <summary>
    /// One vehicle in this many moves suffers a fault.
    /// </summary>
    /// <remarks>
    /// A simulator that always behaves teaches you the wrong thing very convincingly: code written
    /// only against it acquires assumptions — commands are always accepted, the stream never gaps —
    /// that are false on every real installation. Zero disables faults, for tests that need a
    /// clean run.
    /// </remarks>
    [Range(0, 1_000)]
    public int FaultOneMoveIn { get; init; } = 40;
}
