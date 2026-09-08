using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace Labs.Playground;

/// <summary>
/// Demos for the layer where software meets machines — the Modbus wire, tag subscription and
/// backpressure, OEE from an event stream, and AGV traffic deadlock.
/// </summary>
/// <remarks>
/// <para>
/// This is the industrial half of the course made runnable. Everything here works offline with
/// no PLC, no licence and no hardware: the Modbus demo starts a real server on loopback and
/// talks to it over a real socket, and the rest is simulation that is honest about being
/// simulation.
/// </para>
/// <para>
/// What it is <em>not</em> is PLC programming. The machine is somebody else's job. The software
/// above it — dispatching to it, recording what it did, keeping it moving — is this one.
/// </para>
/// </remarks>
public static partial class Program
{
    // ═══════════════════════════════════════════════════════════════════════════════════
    //  modbus — a register map is a spreadsheet, and the wire has no types
    // ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The "documentation" for our imaginary machine. In real life this arrives as an .xls
    /// attachment from an electrical engineer, it is the only copy, and it is wrong in one place.
    /// </summary>
    /// <remarks>
    /// Note the numbering. The spreadsheet says <c>40001</c>; the wire says address <c>0</c>.
    /// The 4xxxx prefix means "holding register" and the rest is one-based. Every Modbus
    /// integration loses an afternoon to this exactly once.
    /// </remarks>
    private static readonly (string Doc, string Meaning)[] ModbusMap =
    [
        ("40001", "machine state   0=idle 1=running 2=blocked 3=starved 4=faulted"),
        ("40002", "pieces produced — HIGH word  ┐ one 32-bit counter"),
        ("40003", "pieces produced — LOW word   ┘ split across two registers"),
        ("40004", "temperature, tenths of a degree C  (signed)"),
    ];

    /// <summary>The machine's actual register file. Sixteen bits each, and that is all they are.</summary>
    private static readonly ushort[] ModbusRegisters =
    [
        0x0001,     // 40001 — running
        0x0001,     // 40002 — pieces, high word
        0x86A3,     // 40003 — pieces, low word     → 0x000186A3 = 100_003
        0x00E1,     // 40004 — 225 tenths           → 22.5 °C
    ];

    private static async Task ModbusAsync()
    {
        Console.WriteLine("Starting a Modbus TCP server on loopback and talking to it over a real");
        Console.WriteLine("socket. No package: the framing below is the protocol, in full.\n");

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(20));

        // Port 0 means "any free port". Hard-coding 502 — the registered Modbus port — is how
        // you find out that something else on the machine already owns it, at the customer site,
        // on a Friday.
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        Task server = ModbusServeAsync(listener, cts.Token);

        using TcpClient client = new();
        await client.ConnectAsync(IPAddress.Loopback, port, cts.Token);
        NetworkStream stream = client.GetStream();

        Console.WriteLine("── What the spreadsheet says ─────────────────────────────────\n");
        foreach ((string doc, string meaning) in ModbusMap)
        {
            Console.WriteLine($"   {doc}   {meaning}");
        }

        Console.WriteLine("\n   Documented as 40001-40004. On the wire that is address 0, count 4.\n");

        // ── One request, byte for byte ───────────────────────────────────────────────
        Console.WriteLine("── Function code 3, read holding registers ───────────────────\n");

        (byte[] request, byte[] response) = await ModbusReadAsync(stream, txId: 1, start: 0, count: 4, cts.Token);

        Console.WriteLine($"   →  {Hex(request)}");
        Console.WriteLine("      ─┬─── ─┬─── ─┬─── ┬─ ┬─ ─┬─── ─┬───");
        Console.WriteLine("       │     │     │    │  │    │     └ quantity  = 4 registers");
        Console.WriteLine("       │     │     │    │  │    └────── start     = 0");
        Console.WriteLine("       │     │     │    │  └─────────── function  = 3");
        Console.WriteLine("       │     │     │    └────────────── unit id   = 1");
        Console.WriteLine("       │     │     └─────────────────── length    = 6 bytes follow");
        Console.WriteLine("       │     └───────────────────────── protocol  = 0 (always)");
        Console.WriteLine("       └─────────────────────────────── transaction id = 1\n");

        Console.WriteLine($"   ←  {Hex(response)}");
        Console.WriteLine("      The last 8 bytes are the payload: four registers, big-endian.\n");

        ushort[] values = ModbusDecode(response);
        Console.WriteLine($"   raw:  {string.Join("  ", values.Select(v => $"0x{v:X4}"))}\n");

        // ── The three ways to misread those bytes ────────────────────────────────────
        Console.WriteLine("── And now the part nobody documents ─────────────────────────\n");

        Console.WriteLine($"   40001 = 0x{values[0]:X4} = {values[0]}");
        Console.WriteLine("      A number. The spreadsheet says it means `running`. Nothing on the");
        Console.WriteLine("      wire says that, and nothing on the wire ever will.\n");

        uint bigEndianWords = ((uint)values[1] << 16) | values[2];
        uint swappedWords = ((uint)values[2] << 16) | values[1];

        Console.WriteLine("   40002/40003 — one counter in two registers:");
        Console.WriteLine($"      high-word-first : {bigEndianWords,12:N0}");
        Console.WriteLine($"      word-swapped    : {swappedWords,12:N0}   ← same bytes");
        Console.WriteLine();
        Console.WriteLine("      Modbus standardises the byte order *inside* a register and says");
        Console.WriteLine("      nothing about the order *between* two of them. Half the devices in");
        Console.WriteLine("      the field do it the other way round. There is no way to tell from");
        Console.WriteLine("      the data which one you are looking at — you find out because the");
        Console.WriteLine("      number is absurd, or worse, because it is plausible.\n");

        double celsius = (short)values[3] / 10.0;
        Console.WriteLine($"   40004 = {values[3]} = {celsius:0.0} °C");
        Console.WriteLine("      There are no floats on this wire and no unit either. It is 22.5 °C");
        Console.WriteLine("      only because a spreadsheet says `tenths`, and it is signed only");
        const ushort minusOneDegree = unchecked((ushort)-10);
        Console.WriteLine("      because the same spreadsheet says so. Cast it wrong and -1 °C reads");
        Console.WriteLine($"      as {minusOneDegree:N0} tenths, which your screen will show as {minusOneDegree / 10.0:0.0} °C.\n");

        // ── The error path ───────────────────────────────────────────────────────────
        Console.WriteLine("── Asking for a register that does not exist ─────────────────\n");

        (byte[] badRequest, byte[] badResponse) = await ModbusReadAsync(stream, txId: 2, start: 9, count: 1, cts.Token);

        Console.WriteLine($"   →  {Hex(badRequest)}     (start = 9)");
        Console.WriteLine($"   ←  {Hex(badResponse)}");
        Console.WriteLine();
        Console.WriteLine($"      function byte came back 0x{badResponse[7]:X2}, which is 0x03 | 0x80.");
        Console.WriteLine("      The high bit set means `this is an exception`, and the byte after it");
        Console.WriteLine($"      is the code: 0x{badResponse[8]:X2} = illegal data address.\n");

        Console.WriteLine("      Note what the protocol does NOT have: no schema, no discovery, no");
        Console.WriteLine("      names, no units, no timestamps, no quality flag. That absence is the");
        Console.WriteLine("      entire argument for OPC UA, where a tag knows it is a temperature in");
        Console.WriteLine("      °C belonging to a named machine — and it is also why Modbus is still");
        Console.WriteLine("      everywhere, because it fits in a device that costs four euro.\n");

        client.Close();
        listener.Stop();
        await server;

        Console.WriteLine("── The rules ─────────────────────────────────────────────────\n");
        Console.WriteLine("   1. The documented number and the wire address differ by one. Always.");
        Console.WriteLine("   2. Word order between registers is a guess until you verify it against");
        Console.WriteLine("      a value you can change and watch.");
        Console.WriteLine("   3. Scaling lives in the spreadsheet, so put it in ONE place in your code");
        Console.WriteLine("      — a tag definition — and never inline `/ 10.0` at a call site.");
        Console.WriteLine("   4. Read the registers you need in ONE request. A per-tag request is how");
        Console.WriteLine("      a supervisor that worked in the office falls over on a real line.");
    }

    /// <summary>Serves Modbus TCP function code 3 until the listener is stopped.</summary>
    private static async Task ModbusServeAsync(TcpListener listener, CancellationToken ct)
    {
        try
        {
            using TcpClient client = await listener.AcceptTcpClientAsync(ct);
            NetworkStream stream = client.GetStream();

            byte[] header = new byte[7];

            while (!ct.IsCancellationRequested)
            {
                // MBAP header: transaction(2) protocol(2) length(2) unit(1).
                await stream.ReadExactlyAsync(header, ct);

                ushort transaction = BinaryPrimitives.ReadUInt16BigEndian(header);
                ushort length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4));
                byte unit = header[6];

                // `length` counts the unit id plus the PDU, which is why it is one more than
                // the PDU length. Getting this wrong desynchronises the stream and every
                // subsequent frame is garbage — the classic "it worked for ten minutes" bug.
                byte[] pdu = new byte[length - 1];
                await stream.ReadExactlyAsync(pdu, ct);

                byte[] responsePdu = ModbusHandle(pdu);

                byte[] frame = new byte[7 + responsePdu.Length];
                BinaryPrimitives.WriteUInt16BigEndian(frame, transaction);
                BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(2), 0);
                BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4), (ushort)(responsePdu.Length + 1));
                frame[6] = unit;
                responsePdu.CopyTo(frame.AsSpan(7));

                await stream.WriteAsync(frame, ct);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or EndOfStreamException or SocketException or ObjectDisposedException)
        {
            // The client hung up or the demo ended. Both are the normal way out of this loop.
        }
    }

    /// <summary>Turns a request PDU into a response PDU, including the exception responses.</summary>
    private static byte[] ModbusHandle(byte[] pdu)
    {
        byte function = pdu[0];

        // 0x01 = illegal function. We only speak "read holding registers".
        if (function != 3)
        {
            return [(byte)(function | 0x80), 0x01];
        }

        ushort start = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(1));
        ushort count = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(3));

        if (start + count > ModbusRegisters.Length)
        {
            return [(byte)(function | 0x80), 0x02];      // illegal data address
        }

        byte[] response = new byte[2 + (count * 2)];
        response[0] = function;
        response[1] = (byte)(count * 2);

        for (int i = 0; i < count; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2 + (i * 2)), ModbusRegisters[start + i]);
        }

        return response;
    }

    /// <summary>Sends one read request and returns both frames, so the caller can print them.</summary>
    private static async Task<(byte[] Request, byte[] Response)> ModbusReadAsync(
        NetworkStream stream, ushort txId, ushort start, ushort count, CancellationToken ct)
    {
        byte[] request = new byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(request, txId);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(4), 6);
        request[6] = 1;                                             // unit id
        request[7] = 3;                                             // function
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(8), start);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(10), count);

        await stream.WriteAsync(request, ct);

        byte[] header = new byte[7];
        await stream.ReadExactlyAsync(header, ct);

        int bodyLength = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4)) - 1;
        byte[] body = new byte[bodyLength];
        await stream.ReadExactlyAsync(body, ct);

        byte[] response = [.. header, .. body];
        return (request, response);
    }

    /// <summary>Pulls the register values out of a successful FC3 response frame.</summary>
    private static ushort[] ModbusDecode(byte[] response)
    {
        int byteCount = response[8];
        ushort[] values = new ushort[byteCount / 2];

        for (int i = 0; i < values.Length; i++)
        {
            values[i] = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(9 + (i * 2)));
        }

        return values;
    }

    private static string Hex(byte[] bytes) => string.Join(' ', bytes.Select(b => b.ToString("X2")));

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  tags — polling loses events, and the machine does not wait for your consumer
    // ═══════════════════════════════════════════════════════════════════════════════════

    private static async Task TagsAsync()
    {
        Console.WriteLine("Two questions that decide whether a line supervisor is trusted:");
        Console.WriteLine("did you see everything, and did you keep up.\n");

        PollingVsSubscription();
        await BackpressureAsync();
    }

    private static void PollingVsSubscription()
    {
        Console.WriteLine("── 1. Polling vs subscription ────────────────────────────────\n");

        // A simulated machine timeline: 300 ticks of 10 ms = 3 seconds of machine time, run
        // instantly. No sleeps, so the result is the same on every machine and in CI — which
        // matters, because the whole point is a race that is normally invisible.
        const int ticks = 300;
        const int tickMs = 10;
        const int faultFrom = 137;      // 1.37 s
        const int faultTo = 143;        // 1.43 s — a 60 ms pulse

        int[] timeline = new int[ticks];
        for (int t = faultFrom; t < faultTo; t++)
        {
            timeline[t] = 4;            // faulted
        }

        for (int t = 0; t < ticks; t++)
        {
            if (timeline[t] == 0)
            {
                timeline[t] = 1;        // running
            }
        }

        // The poller: every 100 ticks = once a second, which is a *generous* poll rate for a
        // supervisor watching a few hundred tags.
        List<int> polled = [];
        for (int t = 0; t < ticks; t += 100)
        {
            polled.Add(timeline[t]);
        }

        // The subscription: the machine tells you when the value changes, so you see the edges.
        List<(int Tick, int State)> changes = [];
        int previous = -1;
        for (int t = 0; t < ticks; t++)
        {
            if (timeline[t] != previous)
            {
                changes.Add((t, timeline[t]));
                previous = timeline[t];
            }
        }

        Console.WriteLine($"   machine time     : {ticks * tickMs} ms");
        Console.WriteLine($"   fault pulse      : {faultFrom * tickMs} ms → {faultTo * tickMs} ms  ({(faultTo - faultFrom) * tickMs} ms long)\n");

        Console.WriteLine($"   polling at 1 Hz  : saw states [{string.Join(", ", polled.Select(StateName))}]");
        Console.WriteLine($"   subscription     : saw {changes.Count} transitions —");
        foreach ((int tick, int state) in changes)
        {
            Console.WriteLine($"                        {tick * tickMs,5} ms  → {StateName(state)}");
        }

        Console.WriteLine();
        Console.WriteLine("   The poller reports a perfect shift. The fault never happened, as far as");
        Console.WriteLine("   your database is concerned, and the maintenance report is wrong — not");
        Console.WriteLine("   approximately wrong, but missing an event entirely.");
        Console.WriteLine();
        Console.WriteLine("   This is why OPC UA and MQTT are subscribe-based, and why `SELECT the");
        Console.WriteLine("   current value every second` is the design that gets a supervisor thrown");
        Console.WriteLine("   out. You cannot poll fast enough: the interesting things on a line are");
        Console.WriteLine("   shorter than your interval, and the ones that matter most are shortest.\n");

        static string StateName(int s) => s switch
        {
            0 => "idle",
            1 => "running",
            2 => "blocked",
            3 => "starved",
            _ => "FAULTED",
        };
    }

    private static async Task BackpressureAsync()
    {
        Console.WriteLine("── 2. When your consumer cannot keep up ──────────────────────\n");

        Console.WriteLine($"   A burst — a pallet arrives and {BurstSize} tags change at once — into a bounded");
        Console.WriteLine("   channel of 16, with a consumer that needs ~15 ms each because it writes to");
        Console.WriteLine("   a database. It cannot keep up. What the channel does about that is a");
        Console.WriteLine("   decision, not a default you can leave alone.\n");

        Console.WriteLine("                 held the      delivered   ids that");
        Console.WriteLine("                 machine for   to the DB   arrived");
        Console.WriteLine("                 ───────────   ─────────   ────────────");

        foreach (BoundedChannelFullMode mode in (BoundedChannelFullMode[])[
            BoundedChannelFullMode.Wait,
            BoundedChannelFullMode.DropOldest,
            BoundedChannelFullMode.DropWrite])
        {
            (int delivered, int first, int last, long blockedMs) = await RunBackpressureAsync(mode);

            Console.WriteLine($"   {mode,-11}   {blockedMs,6} ms      {delivered,3}/{BurstSize}      #{first} … #{last}");
        }

        Console.WriteLine();
        Console.WriteLine("   Wait        — nothing is lost, and the PRODUCER paid for it. Correct for a");
        Console.WriteLine("                 work queue, wrong for a machine: a line does not slow down");
        Console.WriteLine("                 because your reader is busy. That held time is not saved");
        Console.WriteLine("                 anywhere — it becomes a lag behind reality, and then a socket");
        Console.WriteLine("                 buffer overflowing somewhere you cannot see or measure.");
        Console.WriteLine("   DropOldest  — costs the machine nothing and ends on the NEWEST id. You stay");
        Console.WriteLine("                 current and lose history. Right for a live screen.");
        Console.WriteLine("   DropWrite   — costs the machine nothing and stops early: it kept the ids it");
        Console.WriteLine("                 already had and never learned what happened after. You keep");
        Console.WriteLine("                 history and lose the present.");
        Console.WriteLine();
        Console.WriteLine("   Look at the last column rather than the counts. Both drop modes lost the");
        Console.WriteLine("   same *number* of events and lost completely different events, and only one");
        Console.WriteLine("   of those two mistakes is visible on an operator's screen.");
        Console.WriteLine();
        Console.WriteLine("   The real answer on a line is usually both: DropOldest for the screen, and a");
        Console.WriteLine("   separate durable path — a file, a queue, the outbox in module 06 — for the");
        Console.WriteLine("   events you are legally required to still have in three years.");
    }

    private const int BurstSize = 60;

    private static async Task<(int Delivered, int First, int Last, long BlockedMs)> RunBackpressureAsync(BoundedChannelFullMode mode)
    {
        Channel<int> channel = Channel.CreateBounded<int>(new BoundedChannelOptions(16)
        {
            FullMode = mode,
            SingleReader = true,
            SingleWriter = true,
        });

        List<int> delivered = [];

        Task consumer = Task.Run(async () =>
        {
            await foreach (int id in channel.Reader.ReadAllAsync())
            {
                await Task.Delay(15);           // the database write
                delivered.Add(id);
            }
        });

        // The burst. WriteAsync is what blocks under FullMode.Wait; the two drop modes complete
        // instantly and throw one item away without telling anybody — which is why the ids that
        // arrived, rather than any exception, are the only evidence that it happened.
        long start = Stopwatch.GetTimestamp();

        for (int i = 0; i < BurstSize; i++)
        {
            await channel.Writer.WriteAsync(i);
        }

        long blockedMs = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        channel.Writer.Complete();
        await consumer;

        return (delivered.Count, delivered[0], delivered[^1], blockedMs);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  oee — one number, and the argument underneath it
    // ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>One shift, as the events a PLC and an operator terminal would actually record.</summary>
    private readonly record struct ShiftEvent(int Minute, string Kind, int Minutes, string Reason);

    private static void Oee()
    {
        // An eight-hour shift. Everything below is derived from these rows — which is the point:
        // store the events, compute the number. Store the number and you can never answer
        // "why", and you can never recompute it when the definition changes. And it will change.
        ShiftEvent[] shift =
        [
            new(0,   "stop", 20, "changeover to product B   (PLANNED)"),
            new(95,  "stop", 12, "belt jam"),
            new(210, "stop", 30, "break                     (PLANNED)"),
            new(288, "stop", 18, "infeed starved — upstream"),
            new(400, "stop",  7, "label applicator fault"),
        ];

        const int shiftMinutes = 480;
        const int totalCount = 12_400;
        const int goodCount = 12_090;
        const double idealCycleSeconds = 1.8;

        Console.WriteLine("One eight-hour shift, from the event log:\n");
        foreach (ShiftEvent e in shift)
        {
            Console.WriteLine($"   {e.Minute,4} min   {e.Kind}  {e.Minutes,3} min   {e.Reason}");
        }

        Console.WriteLine($"\n   total pieces {totalCount:N0}   ·   good {goodCount:N0}   ·   ideal cycle {idealCycleSeconds} s\n");

        int plannedStops = shift.Where(e => e.Reason.Contains("PLANNED", StringComparison.Ordinal)).Sum(e => e.Minutes);
        int unplannedStops = shift.Where(e => !e.Reason.Contains("PLANNED", StringComparison.Ordinal)).Sum(e => e.Minutes);

        Console.WriteLine("── The formula ───────────────────────────────────────────────\n");
        Console.WriteLine("   OEE = Availability × Performance × Quality\n");

        // ── Reading 1: planned stops come out of the denominator ─────────────────────
        int planned1 = shiftMinutes - plannedStops;
        int run1 = planned1 - unplannedStops;
        Report("planned downtime EXCLUDED", planned1, run1);

        // ── Reading 2: the shift is the shift ────────────────────────────────────────
        int planned2 = shiftMinutes;
        int run2 = shiftMinutes - plannedStops - unplannedStops;
        Report("planned downtime INCLUDED", planned2, run2);

        Console.WriteLine("── Which is right? ───────────────────────────────────────────\n");
        Console.WriteLine("   Both. They answer different questions — `how well did the equipment run");
        Console.WriteLine("   when we asked it to` and `how much of the shift did we get out of it` —");
        Console.WriteLine("   and the standard OEE definition is the first one. The second has a name");
        Console.WriteLine("   of its own, TEEP, when you widen it all the way to calendar time.");
        Console.WriteLine();
        Console.WriteLine("   What matters for you is that the plant manager sees ONE number every");
        Console.WriteLine("   morning and does not know which definition produced it. Whoever decides");
        Console.WriteLine("   what counts as planned downtime moves the number by several points");
        Console.WriteLine("   without touching a machine. Expect that conversation in week two, and");
        Console.WriteLine("   expect to be asked to change it after the number has been on a wall for");
        Console.WriteLine("   a year — which you can only survive if you kept the events.\n");

        Console.WriteLine("── The one that actually hides ───────────────────────────────\n");
        Console.WriteLine("   Micro-stops. A jam cleared in eight seconds is usually below the PLC's");
        Console.WriteLine("   logging threshold, so it never becomes a stop event. The time is still");
        Console.WriteLine("   gone, so it comes out of PERFORMANCE instead of AVAILABILITY — the line");
        Console.WriteLine("   looks slow rather than stopped, and every improvement project aims at");
        Console.WriteLine("   the wrong thing for a year. If Performance is mysteriously low and");
        Console.WriteLine("   nobody can say why, that is where to look first.");
        Console.WriteLine();
        Console.WriteLine("   A Performance figure above 100% is not a good day either. It means the");
        Console.WriteLine("   ideal cycle time in your configuration is wrong, or somebody ran the");
        Console.WriteLine("   line above its rated speed. Clamp it and raise it as a data-quality");
        Console.WriteLine("   alarm; never quietly display 104%.");

        void Report(string label, int plannedMinutes, int runMinutes)
        {
            double availability = (double)runMinutes / plannedMinutes;
            double performance = idealCycleSeconds * totalCount / 60.0 / runMinutes;
            double quality = (double)goodCount / totalCount;
            double oee = availability * performance * quality;

            Console.WriteLine($"   {label}");
            Console.WriteLine($"      planned production time  {plannedMinutes,4} min      run time {runMinutes,4} min");
            Console.WriteLine($"      Availability {availability,6:P1}   Performance {performance,6:P1}   Quality {quality,6:P1}");
            Console.WriteLine($"      →  OEE {oee,6:P1}\n");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  traffic — two vehicles, one aisle, and the 03:00 phone call
    // ═══════════════════════════════════════════════════════════════════════════════════

    private static async Task TrafficAsync()
    {
        Console.WriteLine("A transport order is `move this load unit from A to B`. To do it the");
        Console.WriteLine("vehicle must own every zone on its route at once — otherwise two vehicles");
        Console.WriteLine("meet in an aisle that fits one.\n");

        await NaiveDeadlockAsync();
        await OrderedAcquisitionAsync();
        await GranularityAsync();

        Console.WriteLine("── What a real traffic manager does ──────────────────────────\n");
        Console.WriteLine("   1. A total order on zone ids, or an allocator that grants a whole route");
        Console.WriteLine("      atomically. Either removes the cycle; neither is optional.");
        Console.WriteLine("   2. All-or-nothing plus retry introduces STARVATION — the unlucky vehicle");
        Console.WriteLine("      retries forever while others slip past. Queue the requests FIFO.");
        Console.WriteLine("   3. Zone size is the throughput dial, and it is the whole engineering");
        Console.WriteLine("      argument: too coarse and vehicles queue for aisles they never enter,");
        Console.WriteLine("      too fine and you spend the gain on allocator traffic — and you get");
        Console.WriteLine("      more chances to deadlock across zone boundaries.");
        Console.WriteLine("   4. Nothing times out. Two vehicles that will never move are not an");
        Console.WriteLine("      exception in a log; they are a stopped line, so the deadlock must be");
        Console.WriteLine("      prevented by construction, not detected and recovered from.");
    }

    private static async Task NaiveDeadlockAsync()
    {
        Console.WriteLine("── 1. Acquire in the order the route needs ───────────────────\n");

        SemaphoreSlim[] zones = [new(1, 1), new(1, 1)];

        // V1 goes north through zone 0 then 1. V2 goes south, so it meets them the other way
        // round. Both are behaving perfectly reasonably.
        Task<bool> v1 = MoveAsync("V1", zones, first: 0, second: 1);
        Task<bool> v2 = MoveAsync("V2", zones, first: 1, second: 0);

        bool[] results = await Task.WhenAll(v1, v2);

        Console.WriteLine($"\n   completed: {results.Count(r => r)} of 2\n");
        Console.WriteLine("   V1 holds zone 0 and waits for 1. V2 holds zone 1 and waits for 0.");
        Console.WriteLine("   Neither will ever let go, because letting go is not in the plan.");
        Console.WriteLine();
        Console.WriteLine("   The timeout above exists so this demo ends. On a real floor there is no");
        Console.WriteLine("   timeout — the vehicles sit in the aisle with their lights on, the line");
        Console.WriteLine("   behind them backs up, and somebody phones you.\n");

        static async Task<bool> MoveAsync(string name, SemaphoreSlim[] zones, int first, int second)
        {
            if (!await zones[first].WaitAsync(TimeSpan.FromMilliseconds(300)))
            {
                return false;
            }

            Console.WriteLine($"   {name} took zone {first}");

            try
            {
                await Task.Delay(50);       // driving through it

                if (!await zones[second].WaitAsync(TimeSpan.FromMilliseconds(300)))
                {
                    Console.WriteLine($"   {name} STUCK waiting for zone {second}");
                    return false;
                }

                try
                {
                    Console.WriteLine($"   {name} took zone {second} — through");
                    return true;
                }
                finally
                {
                    zones[second].Release();
                }
            }
            finally
            {
                zones[first].Release();
            }
        }
    }

    private static async Task OrderedAcquisitionAsync()
    {
        Console.WriteLine("── 2. The same two moves, lowest zone id first ───────────────\n");

        SemaphoreSlim[] zones = [new(1, 1), new(1, 1)];

        Task<bool> v1 = MoveOrderedAsync("V1", zones, 0, 1);
        Task<bool> v2 = MoveOrderedAsync("V2", zones, 1, 0);

        bool[] results = await Task.WhenAll(v1, v2);

        Console.WriteLine($"\n   completed: {results.Count(r => r)} of 2\n");
        Console.WriteLine("   One line of difference: sort the zones before taking them. A cycle in");
        Console.WriteLine("   the wait-for graph needs two vehicles holding in opposite orders, and if");
        Console.WriteLine("   everybody acquires ascending, opposite orders cannot happen.");
        Console.WriteLine();
        Console.WriteLine("   V2 now waits before it enters rather than halfway through, which is also");
        Console.WriteLine("   the physically safer place to be blocked.\n");

        static async Task<bool> MoveOrderedAsync(string name, SemaphoreSlim[] zones, params int[] route)
        {
            int[] order = [.. route.Order()];
            List<int> held = [];

            try
            {
                foreach (int z in order)
                {
                    if (!await zones[z].WaitAsync(TimeSpan.FromSeconds(2)))
                    {
                        return false;
                    }

                    held.Add(z);
                }

                Console.WriteLine($"   {name} route {string.Join("→", route)}, took [{string.Join(", ", order)}] — through");
                await Task.Delay(50);
                return true;
            }
            finally
            {
                foreach (int z in held)
                {
                    zones[z].Release();
                }
            }
        }
    }

    private static async Task GranularityAsync()
    {
        Console.WriteLine("── 3. Zone size is the throughput dial ───────────────────────\n");

        Console.WriteLine("   Six vehicles, four moves each, ordered acquisition throughout — so no");
        Console.WriteLine("   deadlock either way. The only thing that changes is how finely the same");
        Console.WriteLine("   floor is cut into zones. (The routes are drawn from each layout, so they");
        Console.WriteLine("   are not the identical pairs — what is identical is the amount of driving:");
        Console.WriteLine("   24 moves of 20 ms, whichever way the floor is divided.)\n");

        foreach (int zoneCount in (int[])[3, 12])
        {
            long ms = await RunFleetAsync(zoneCount);
            Console.WriteLine($"   {zoneCount,2} zones   fleet finished in {ms,5} ms");
        }

        Console.WriteLine();
        Console.WriteLine("   Same vehicles, same driving, same algorithm. Coarse zones serialise moves");
        Console.WriteLine("   that never actually conflict, and the floor runs at a fraction of what");
        Console.WriteLine("   it could. This is what `throughput optimisation` means in a WCS job");
        Console.WriteLine("   advert, and it is mostly this decision rather than clever code.\n");

        static async Task<long> RunFleetAsync(int zoneCount)
        {
            SemaphoreSlim[] zones = [.. Enumerable.Range(0, zoneCount).Select(_ => new SemaphoreSlim(1, 1))];

            long start = Stopwatch.GetTimestamp();

            await Task.WhenAll(Enumerable.Range(0, 6).Select(async vehicle =>
            {
                // Fixed seed per vehicle: same routes on every run and on every machine, so the
                // comparison is between the two zone layouts and not between two dice rolls.
                Random random = new(vehicle);

                for (int move = 0; move < 4; move++)
                {
                    int a = random.Next(zoneCount);
                    int b = random.Next(zoneCount);
                    int[] route = a == b ? [a] : [Math.Min(a, b), Math.Max(a, b)];

                    List<int> held = [];
                    try
                    {
                        foreach (int z in route)
                        {
                            await zones[z].WaitAsync();
                            held.Add(z);
                        }

                        await Task.Delay(20);       // driving
                    }
                    finally
                    {
                        foreach (int z in held)
                        {
                            zones[z].Release();
                        }
                    }
                }
            }));

            return (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
    }
}
