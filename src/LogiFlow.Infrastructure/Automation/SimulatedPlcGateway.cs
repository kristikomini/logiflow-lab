using System.Runtime.CompilerServices;
using System.Threading.Channels;
using LogiFlow.Application.Abstractions.Automation;
using LogiFlow.Domain.Automation;
using Microsoft.Extensions.Logging;

namespace LogiFlow.Infrastructure.Automation;

/// <summary>
/// A warehouse that does not exist, behaving enough like one to develop and test against.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a deliverable, not scaffolding.</b> The instinct from web work is that the fake
/// implementation is something to delete once the real one exists. Here it is the opposite: the
/// simulator outlives the project, because it is the only environment in which the same scenario
/// can be run a thousand times. On a real line you get four hours on a Sunday.
/// </para>
/// <para>
/// <b>What it models.</b> Vehicles occupying zones, travel taking time, commands being accepted or
/// refused, faults latching until somebody acknowledges them, a scan-cycle lag between when a
/// value was true and when we hear about it, and a bounded telemetry buffer that drops rather than
/// blocking.
/// </para>
/// <para>
/// <b>What it does NOT model, which is the more useful list.</b> No packet loss or reordering — a
/// real gateway that buffers through a dropped link delivers a burst out of order afterwards. No
/// electrical noise or stuck-at sensor values. No partial or torn reads. No clock drift on the
/// device, so <see cref="TelemetrySample.Age"/> here is always exactly the configured scan lag,
/// where a real fleet has one machine eleven minutes out. Every one of those is a class of bug
/// this simulator will never catch, and knowing that is worth more than a simulator which quietly
/// pretends to be complete.
/// </para>
/// <para>
/// <b>Time is injected, and nothing here sleeps.</b> <see cref="TickAsync"/> advances the world by
/// one step using whatever <see cref="TimeProvider"/> it was given, so a test with
/// <c>FakeTimeProvider</c> can run a simulated hour in milliseconds and get the same answer every
/// time. A hosted driver calls the same method on a real timer in production. That separation is
/// what makes a throughput assertion a test rather than an afternoon.
/// </para>
/// Covered in: <c>course/module-28-industrial-and-ot/01-the-boundary.md</c>
/// </remarks>
public sealed class SimulatedPlcGateway : IEquipmentGateway
{
    /// <summary>
    /// How far behind reality a reading is when we get it.
    /// </summary>
    /// <remarks>
    /// A real value is already old by the time a server has it: the PLC scan that read the sensor
    /// finished some milliseconds ago, and then it travelled. Modelling that here is what makes
    /// <see cref="TelemetrySample.Age"/> a number worth alarming on rather than a constant zero,
    /// and it is why the source and receive timestamps are two fields instead of one.
    /// </remarks>
    private static readonly TimeSpan ScanLag = TimeSpan.FromMilliseconds(40);

    private readonly SimulationOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SimulatedPlcGateway> _logger;
    private readonly Vehicle[] _vehicles;
    private readonly Channel<TelemetrySample> _telemetry;
    private readonly Random _random;

    private long _dropped;
    private long _movesStarted;

    /// <summary>Creates a simulated floor.</summary>
    /// <param name="options">Shape of the warehouse.</param>
    /// <param name="timeProvider">The clock. <c>FakeTimeProvider</c> in tests.</param>
    /// <param name="logger">Logger.</param>
    public SimulatedPlcGateway(SimulationOptions options, TimeProvider timeProvider, ILogger<SimulatedPlcGateway> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
        _random = new Random(options.Seed);

        _vehicles = [.. Enumerable.Range(0, options.Vehicles).Select(i => new Vehicle
        {
            Id = EquipmentId.New(),
            Code = $"LGV-{i + 1:00}",
            Zone = ZoneName(i % options.Zones),
            State = EquipmentState.Idle,
        })];

        Fleet = [.. _vehicles.Select(v => new EquipmentDescriptor(v.Id, v.Code, EquipmentKind.Agv))];

        // Bounded, and DropOldest. The choice matters and is the reason it is spelled out rather
        // than defaulted: a machine does not slow down because a consumer is busy, so blocking the
        // producer would not prevent loss — it would move the loss somewhere with no counter on
        // it, and eventually into a buffer nobody can see. Dropping the oldest keeps the live view
        // current and loses history, which is the right trade for a screen. Anything that must
        // survive goes down a separate durable path.
        _telemetry = Channel.CreateBounded<TelemetrySample>(
            new BoundedChannelOptions(256)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true,
            },
            // The itemDropped callback is the ONLY way to know a DropOldest channel discarded
            // something. TryWrite returns true whether or not it threw a sample away, so code
            // that checks the return value is counting nothing while looking like it counts.
            itemDropped: _ => Interlocked.Increment(ref _dropped));
    }

    /// <inheritdoc />
    public string Description => $"simulated PLC — {_vehicles.Length} vehicles, {_options.Zones} zones, seed {_options.Seed}";

    /// <inheritdoc />
    public IReadOnlyList<EquipmentDescriptor> Fleet { get; }

    /// <summary>
    /// How many samples the telemetry buffer has thrown away.
    /// </summary>
    /// <remarks>
    /// <b>A channel that silently discards is a failure mode invisible by construction.</b>
    /// <c>DropOldest</c> throws data away with no exception, no log line and no return value, so
    /// the only evidence it ever happened is a counter somebody deliberately kept. This is that
    /// counter, and in a real deployment it belongs on the dashboard next to throughput.
    /// </remarks>
    public long DroppedSamples => Interlocked.Read(ref _dropped);

    /// <summary>Every zone on the simulated floor, in order.</summary>
    public IReadOnlyList<string> Zones => [.. Enumerable.Range(0, _options.Zones).Select(ZoneName)];

    /// <summary>Where each vehicle physically is, for rebuilding an allocator after a restart.</summary>
    public IReadOnlyDictionary<string, EquipmentId> Occupancy =>
        _vehicles.ToDictionary(v => v.Zone, v => v.Id, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Takes the next buffered sample without awaiting, for a test that pumps the loop by hand.
    /// </summary>
    /// <remarks>
    /// The same channel <see cref="SubscribeAsync"/> reads, so use one or the other rather than
    /// both — the channel is configured single-reader and two consumers would race for samples.
    /// A test that drove a background reader task instead would be exactly the flaky test this
    /// whole design exists to avoid.
    /// </remarks>
    /// <param name="sample">The sample, when one was buffered.</param>
    public bool TryReadSample(out TelemetrySample sample) => _telemetry.Reader.TryRead(out sample);

    /// <inheritdoc />
    public async IAsyncEnumerable<TelemetrySample> SubscribeAsync([EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (TelemetrySample sample in _telemetry.Reader.ReadAllAsync(ct))
        {
            yield return sample;
        }
    }

    /// <inheritdoc />
    public Task<CommandAck> SendAsync(EquipmentCommand command, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        Vehicle? vehicle = Array.Find(_vehicles, v => v.Id.Equals(command.EquipmentId));
        if (vehicle is null)
        {
            return Task.FromResult(CommandAck.Refused($"No equipment '{command.EquipmentId}' on this gateway."));
        }

        if (!string.Equals(command.Verb, "move", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(CommandAck.Refused($"This equipment does not understand '{command.Verb}'."));
        }

        if (vehicle.State == EquipmentState.Faulted)
        {
            return Task.FromResult(CommandAck.Refused($"{vehicle.Code} is faulted and will not move."));
        }

        if (string.IsNullOrWhiteSpace(command.Argument))
        {
            return Task.FromResult(CommandAck.Refused("A move needs a destination zone."));
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();

        vehicle.Destination = command.Argument;
        vehicle.ArrivesAtUtc = now.AddMilliseconds(_options.TravelMilliseconds);
        Transition(vehicle, EquipmentState.Running, now);

        Interlocked.Increment(ref _movesStarted);

        // The acknowledgement means "I have the instruction", not "it is done". The pallet is
        // still in the aisle; the outcome will arrive on the telemetry stream like everything else.
        return Task.FromResult(CommandAck.Ok());
    }

    /// <inheritdoc />
    public Task<CommandAck> AcknowledgeAsync(EquipmentId equipmentId, string acknowledgedBy, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        Vehicle? vehicle = Array.Find(_vehicles, v => v.Id.Equals(equipmentId));
        if (vehicle is null)
        {
            return Task.FromResult(CommandAck.Refused($"No equipment '{equipmentId}' on this gateway."));
        }

        if (vehicle.State != EquipmentState.Faulted)
        {
            return Task.FromResult(CommandAck.Refused($"{vehicle.Code} has no latched fault."));
        }

        _logger.LogInformation("Fault on {Code} acknowledged by {Person}", vehicle.Code, acknowledgedBy);

        // Acknowledging returns the machine to Idle, NOT to Running. Permission to run again is
        // not a start command, and collapsing the two removes a human checkpoint that exists on
        // purpose — see Equipment.Acknowledge for the same argument on the domain side.
        Transition(vehicle, EquipmentState.Idle, _timeProvider.GetUtcNow());
        vehicle.Destination = null;
        vehicle.ArrivesAtUtc = null;

        return Task.FromResult(CommandAck.Ok());
    }

    /// <summary>
    /// Advances the simulated world to whatever the clock now says, emitting whatever changed.
    /// </summary>
    /// <remarks>
    /// Deliberately not on <see cref="IEquipmentGateway"/>: nothing drives a real conveyor forward
    /// by calling a method, and putting a tick on the port would let application code depend on
    /// something only the fake can do. The driver knows it has a simulator; the dispatcher does not.
    /// </remarks>
    /// <param name="ct">Cancellation.</param>
    public Task TickAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        DateTimeOffset now = _timeProvider.GetUtcNow();

        foreach (Vehicle vehicle in _vehicles)
        {
            if (vehicle.State != EquipmentState.Running || vehicle.ArrivesAtUtc > now)
            {
                continue;
            }

            // A fault interrupts the move, and the vehicle stays where the fault happened rather
            // than teleporting to its destination. That matters: the zones it holds are still
            // occupied, and a dispatcher that assumed arrival would strand a pallet.
            if (_options.FaultOneMoveIn > 0 && _random.Next(_options.FaultOneMoveIn) == 0)
            {
                vehicle.Destination = null;
                vehicle.ArrivesAtUtc = null;
                Transition(vehicle, EquipmentState.Faulted, now, "photocell obstructed");
                continue;
            }

            vehicle.Zone = vehicle.Destination!;
            vehicle.Destination = null;
            vehicle.ArrivesAtUtc = null;
            Transition(vehicle, EquipmentState.Idle, now);
        }

        return Task.CompletedTask;
    }

    /// <summary>How many move commands have been accepted. For commissioning assertions.</summary>
    public long MovesStarted => Interlocked.Read(ref _movesStarted);

    private static string ZoneName(int index) => $"Z{index + 1:00}";

    /// <summary>
    /// Changes a vehicle's state and publishes it — but only on an actual change.
    /// </summary>
    /// <remarks>
    /// This is what makes the stream a subscription rather than a poll in disguise. Republishing
    /// an unchanged value every tick would give consumers a stop event of zero length on every
    /// tick and make the change stream useless for computing availability.
    /// </remarks>
    private void Transition(Vehicle vehicle, EquipmentState state, DateTimeOffset now, string? fault = null)
    {
        if (vehicle.State == state && fault is null)
        {
            return;
        }

        vehicle.State = state;

        Publish(new TelemetrySample(
            vehicle.Id,
            "state",
            (double)state,
            Quality.Good,
            SourceTimestampUtc: now - ScanLag,
            ReceivedAtUtc: now));

        if (fault is not null)
        {
            _logger.LogWarning("{Code} faulted: {Reason}", vehicle.Code, fault);
        }
    }

    private void Publish(TelemetrySample sample) => _telemetry.Writer.TryWrite(sample);

    private sealed class Vehicle
    {
        public required EquipmentId Id { get; init; }

        public required string Code { get; init; }

        public required string Zone { get; set; }

        public required EquipmentState State { get; set; }

        public string? Destination { get; set; }

        public DateTimeOffset? ArrivesAtUtc { get; set; }
    }
}
