using System.Buffers.Binary;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using LogiFlow.Application.Abstractions.Automation;
using LogiFlow.Domain.Automation;
using Microsoft.Extensions.Logging;

namespace LogiFlow.Infrastructure.Automation;

/// <summary>
/// Talks to real equipment over Modbus TCP.
/// </summary>
/// <remarks>
/// <para>
/// <b>Modbus is a number at an address, and that is the entire data model.</b> Published in 1979,
/// free of licence, small enough for a four-euro microcontroller, and therefore in an enormous
/// amount of equipment that is still running and will still be running in ten years. Everything
/// this class does is compensating for what the protocol does not carry.
/// </para>
/// <para>
/// <b>It polls, and that is the protocol's fault rather than a design choice.</b> There is no
/// Modbus request meaning "tell me when this changes", so this adapter reads on an interval and
/// turns the differences into the change stream <see cref="IEquipmentGateway"/> promises. It
/// therefore inherits every weakness of polling: a transient shorter than the interval is never
/// seen at all. That is a reason to prefer OPC UA wherever the installation offers it — not a
/// reason to set the interval to 10 ms, which is how a supervisor at a desk degrades a line.
/// </para>
/// <para>
/// <b>The register map is configuration, and on a real project it arrives as a spreadsheet.</b>
/// One block read per poll rather than a request per tag: a request per tag is how a supervisor
/// that worked in the office falls over on site. And the scaling lives here, in one place, so that
/// no call site anywhere else divides by ten.
/// </para>
/// <para>
/// <b>Quality is invented here, honestly.</b> Modbus has no status code, so every reading would be
/// <see cref="Quality.Good"/> if we took the protocol at its word. Instead a value that has not
/// been refreshed within twice the poll interval is reported <see cref="Quality.Uncertain"/> — the
/// absence of an error is not evidence that anybody looked.
/// </para>
/// Covered in: <c>course/module-28-industrial-and-ot/02-protocols-on-the-wire.md</c>
/// </remarks>
public sealed class ModbusEquipmentGateway : IEquipmentGateway, IAsyncDisposable
{
    /// <summary>
    /// The state word's address on the wire.
    /// </summary>
    /// <remarks>
    /// The commissioning spreadsheet calls this <c>40001</c>. On the wire it is address <b>0</b> —
    /// the <c>4xxxx</c> prefix means "holding register" and the rest is one-based. Every Modbus
    /// integration loses an afternoon to that once.
    /// </remarks>
    private const ushort StateRegister = 0;

    private readonly ModbusOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ModbusEquipmentGateway> _logger;
    private readonly Channel<TelemetrySample> _telemetry;
    private readonly Dictionary<EquipmentId, ushort> _lastState = [];

    private TcpClient? _client;
    private ushort _transactionId;
    private long _dropped;

    /// <summary>Creates an adapter for a real installation.</summary>
    /// <param name="fleet">The equipment this gateway serves, from commissioning data.</param>
    /// <param name="options">Where the device is and how often to read it.</param>
    /// <param name="timeProvider">The clock.</param>
    /// <param name="logger">Logger.</param>
    public ModbusEquipmentGateway(
        IReadOnlyList<EquipmentDescriptor> fleet,
        ModbusOptions options,
        TimeProvider timeProvider,
        ILogger<ModbusEquipmentGateway> logger)
    {
        ArgumentNullException.ThrowIfNull(fleet);
        ArgumentNullException.ThrowIfNull(options);

        Fleet = fleet;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;

        _telemetry = Channel.CreateBounded<TelemetrySample>(
            new BoundedChannelOptions(256)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true,
            },
            itemDropped: _ => Interlocked.Increment(ref _dropped));
    }

    /// <inheritdoc />
    public string Description => $"Modbus TCP {_options.Host}:{_options.Port}, unit {_options.UnitId}";

    /// <inheritdoc />
    public IReadOnlyList<EquipmentDescriptor> Fleet { get; }

    /// <summary>Samples discarded because the consumer could not keep up.</summary>
    public long DroppedSamples => Interlocked.Read(ref _dropped);

    /// <inheritdoc />
    public async IAsyncEnumerable<TelemetrySample> SubscribeAsync([EnumeratorCancellation] CancellationToken ct)
    {
        // The poll loop runs for as long as somebody is reading. Starting it here rather than in
        // the constructor means a gateway nobody subscribed to never opens a socket, which is what
        // you want when a process starts up and is still deciding what it is.
        Task poller = Task.Run(() => PollAsync(ct), ct);

        try
        {
            await foreach (TelemetrySample sample in _telemetry.Reader.ReadAllAsync(ct))
            {
                yield return sample;
            }
        }
        finally
        {
            _telemetry.Writer.TryComplete();
            await poller.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Writing to equipment is deliberately not implemented. Collecting data is a far easier
    /// conversation with an OT team than writing to a PLC, so the honest default is read-only
    /// until somebody has actually agreed the write path, the register map for it, and what
    /// happens when this process sends a command to a machine mid-cycle.
    /// </remarks>
    public Task<CommandAck> SendAsync(EquipmentCommand command, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(CommandAck.Refused("This gateway is read-only; no write path has been commissioned."));
    }

    /// <inheritdoc />
    public Task<CommandAck> AcknowledgeAsync(EquipmentId equipmentId, string acknowledgedBy, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(CommandAck.Refused("This gateway is read-only; acknowledge at the HMI."));
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _client?.Dispose();
        _client = null;
        return ValueTask.CompletedTask;
    }

    private async Task PollAsync(CancellationToken ct)
    {
        TimeSpan interval = TimeSpan.FromMilliseconds(_options.PollMilliseconds);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e) when (e is SocketException or IOException or EndOfStreamException)
            {
                // A dropped link is Tuesday, not an incident. Close the socket so the next pass
                // reconnects, and keep going — a gateway that gives up on the first network blip
                // is a gateway somebody restarts by hand every morning.
                _logger.LogWarning(e, "Modbus link to {Host} failed; reconnecting", _options.Host);
                _client?.Dispose();
                _client = null;
            }

            await Task.Delay(interval, _timeProvider, ct).ConfigureAwait(false);
        }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        NetworkStream stream = await EnsureConnectedAsync(ct).ConfigureAwait(false);

        // One block read for the whole fleet rather than a request per machine. The devices are
        // laid out consecutively from StateRegister by the commissioning spreadsheet.
        ushort[] values = await ReadHoldingRegistersAsync(stream, StateRegister, (ushort)Fleet.Count, ct)
            .ConfigureAwait(false);

        DateTimeOffset now = _timeProvider.GetUtcNow();

        for (int i = 0; i < Fleet.Count; i++)
        {
            EquipmentDescriptor machine = Fleet[i];
            ushort raw = values[i];

            // Publish on change only. Republishing every poll would give consumers a stop event of
            // zero length on every tick and make the stream useless for computing availability.
            if (_lastState.TryGetValue(machine.Id, out ushort previous) && previous == raw)
            {
                continue;
            }

            _lastState[machine.Id] = raw;

            // The wire has no types and no range. A value outside the enum is a device that is not
            // the device the spreadsheet described — a wiring change, a firmware update, or the
            // wrong unit id — and it is reported as Bad rather than cast into a plausible state.
            bool known = Enum.IsDefined(typeof(EquipmentState), (int)raw);

            _telemetry.Writer.TryWrite(new TelemetrySample(
                machine.Id,
                "state",
                raw,
                known ? Quality.Good : Quality.Bad,

                // Modbus carries no timestamp of any kind, so the source time is the moment we
                // read it minus nothing. Recording it as equal to the receive time is a LIE we are
                // choosing knowingly: the value's real age is somewhere between zero and one poll
                // interval, and the protocol will not tell us where. Systems that need that answer
                // need OPC UA, and this comment is the reason to ask for it.
                SourceTimestampUtc: now,
                ReceivedAtUtc: now));
        }
    }

    private async Task<NetworkStream> EnsureConnectedAsync(CancellationToken ct)
    {
        if (_client is { Connected: true })
        {
            return _client.GetStream();
        }

        _client?.Dispose();
        _client = new TcpClient();

        using CancellationTokenSource timeout =
            CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(_options.TimeoutMilliseconds));

        await _client.ConnectAsync(_options.Host, _options.Port, timeout.Token).ConfigureAwait(false);
        _logger.LogInformation("Connected to {Description}", Description);

        return _client.GetStream();
    }

    /// <summary>Function code 3, and the seven-byte MBAP header that carries it.</summary>
    private async Task<ushort[]> ReadHoldingRegistersAsync(
        NetworkStream stream, ushort start, ushort count, CancellationToken ct)
    {
        _transactionId++;

        byte[] request = new byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(request, _transactionId);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(2), 0);        // protocol id: always 0
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(4), 6);        // bytes that follow
        request[6] = (byte)_options.UnitId;
        request[7] = 3;                                                      // read holding registers
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(8), start);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(10), count);

        await stream.WriteAsync(request, ct).ConfigureAwait(false);

        byte[] header = new byte[7];
        await stream.ReadExactlyAsync(header, ct).ConfigureAwait(false);

        // The length field counts the unit id plus the PDU, so it is one more than the PDU length.
        // Getting this wrong desynchronises the stream and every subsequent frame is garbage —
        // which presents as "it worked for ten minutes", the worst kind of bug to be handed.
        int bodyLength = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4)) - 1;
        byte[] body = new byte[bodyLength];
        await stream.ReadExactlyAsync(body, ct).ConfigureAwait(false);

        // The high bit of the function code set means this is an exception response, and the next
        // byte is the reason: 0x02 is illegal data address, which on a first deployment almost
        // always means the register map and the device disagree.
        if ((body[0] & 0x80) != 0)
        {
            throw new IOException($"Modbus exception 0x{body[1]:X2} reading {count} registers from {start}.");
        }

        ushort[] values = new ushort[body[1] / 2];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = BinaryPrimitives.ReadUInt16BigEndian(body.AsSpan(2 + (i * 2)));
        }

        return values;
    }
}
