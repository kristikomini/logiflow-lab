# The edge, and reading the advert

> Below the PLC there is another layer again: a byte stream on a wire, a bus with no addresses, and
> a box with no operating system. Some of it is yours. Most of it is not. This chapter is about
> which is which — because around Modena the word *embedded* appears in two completely different
> adverts, and answering the wrong one wastes an application.

Two demos, and neither needs hardware:

```bash
cd labs/Labs.Playground
dotnet run serial
dotnet run can
```

---

## 1. Two adverts that use the same word

Search *embedded* in the province and you get two populations. They look similar in the first
paragraph and they diverge completely by the third.

**Advert A — a .NET job with a wire attached.**

> *Sviluppatore C#/.NET per software di supervisione e interfacciamento macchine. Conoscenza di
> protocolli seriali (Modbus RTU), gestione di dispositivi, ambiente industriale.*

The software runs on a PC or an industrial panel, under Windows, in C#. The *embedded* part is
what it talks to, not what it runs on. **This is your job.** The whole of module 28 applies, and
this chapter is the last mile of it.

**Advert B — a firmware job.**

> *Sviluppatore firmware embedded. C/C++ su microcontrollori STM32, FreeRTOS, sviluppo driver,
> schemi elettrici, oscilloscopio, debug JTAG.*

This is not a .NET job with extra steps. It is a different language, a different toolchain, a
different degree in many cases, and a different career. **Do not apply, and do not claim it.**

### The vocabulary that tells you which one you are reading

Scan for these before you read anything else. Three or more from the right-hand column and you can
stop reading.

| Advert A — apply | Advert B — skip |
|---|---|
| C#, .NET, VB.NET, WPF, WinForms | C, C++, assembly |
| supervisione, interfacciamento, SCADA, MES | firmware, bare metal, driver |
| Modbus RTU/TCP, OPC UA, seriale, RS-485 | FreeRTOS, Zephyr, RTOS, scheduler |
| PC industriale, panel PC, Windows IoT | STM32, PIC, ESP32, microcontrollore |
| SQL Server, database, reportistica | JTAG, oscilloscopio, saldatura, schemi |
| "dialogo con PLC" | "sviluppo scheda", "hardware"|

**The reliable single test: what does the code run on?** A PC — yours. A chip — not yours.

There is a third population, smaller and worth knowing about: **Advert C**, the Linux edge gateway.
.NET on ARM, MQTT northbound, Modbus southbound, Docker on a fanless box. That one *is* yours, and
it pays better than A because fewer .NET developers will touch anything without a Start menu.

---

## 2. A byte stream has no messages

This is the single most common bug in device integration, and it survives every test written at a
desk.

`SerialPort.Read` and `NetworkStream.Read` make exactly one promise: *some bytes that have
arrived*. Not your message. Not one message. Not a whole message.

```csharp
int n = stream.Read(buffer);                            // <- the bug
Handle(Encoding.ASCII.GetString(buffer, 0, n));
```

Run `dotnet run serial` and watch a weighing scale send three readings that arrive in **one** read,
and a fourth that arrives in **two pieces 120 ms apart**. The naive loop logs one weight, silently
loses two, and truncates the fourth.

The fix is not a bigger buffer. It is to stop thinking in reads:

1. Append whatever arrived to a buffer you keep **across** reads.
2. Scan that buffer for a complete frame — a start byte, an end byte, a length prefix, a newline;
   whatever this device uses.
3. Take complete frames out and leave the remainder alone.
4. Discard anything before the first start byte. On a line that was already running when you
   opened the port, the first thing you read is the tail of somebody else's frame.

The demo ends with five bytes still in the buffer, which is the normal state of affairs rather than
an error.

### The checksum is weak, and it is there for a reason

Most serial devices use an LRC — one byte, the XOR of the body — or a CRC-16. It is not
cryptography and it is not trying to be. A serial cable is an analogue thing running past a
frequency converter, and it will occasionally deliver a byte nobody sent. Verify it, count the
failures, and never treat a failed checksum as a fatal error: on a healthy line it happens, and on
a sick line the *rate* is your diagnostic.

---

## 3. RS-232, RS-485, and the bug that only appears at the customer

The advert says *seriale* and means one of two incompatible things.

- **RS-232** — point to point, full duplex. One device, one port. Everything in section 2 works.
- **RS-485** — a party line: up to 32 devices on one pair of wires, **half duplex**. Exactly one
  device may transmit at a time, and nothing enforces that.

RS-485 is a polling protocol by necessity. The master asks; the master then *shuts up* long enough
for the slave to turn the line around — conventionally 3.5 character times of silence. Send the
next request too early and two drivers are on the same wire, and both messages become noise.

**This is why a working integration fails at the customer.** At your desk there is one device, the
gaps are enormous, and nothing collides. On site there are four devices, a longer cable, and a
tighter poll loop. The symptom is intermittent checksum errors, not a crash, and it is a *timing*
bug in code that looks correct.

Two things follow that are worth saying in an interview:

- **One port, one owner, one thread.** Two threads writing to the same RS-485 port is not a race
  you can fix with a lock around `Write`, because the unit of exclusion is the request *and* its
  answer.
- **Termination and biasing are real.** When somebody on site says the bus needs terminating
  resistors, that is not folklore, and it is not your department — but recognising the sentence
  saves an afternoon of reading your own code.

---

## 4. CAN, where the number is the priority

`dotnet run can`. Around Modena this is Motor Valley vocabulary — test benches, vehicle data
logging, agricultural and construction machinery — and it shows up in .NET adverts as *acquisizione
dati CAN*, usually behind a vendor USB-CAN adapter with a C# API.

Three things make it unlike anything in web work:

**There are no addresses.** Nothing in a frame says who sent it or who it is for. The 11-bit
identifier names the *message* — "engine data", "wheel speeds" — and every node hears every frame
and decides for itself whether it cares. You do not request a value; you listen for it.

**Priority is the identifier, and arbitration is electrical.** When two nodes start transmitting in
the same microsecond, each watches the wire as it writes: a node that sends a 1 and reads a 0 has
lost, and it stops immediately. The lower identifier always wins, the winner's message is not
corrupted, and no time is lost. The demo walks the bits.

The consequence matters more than the mechanism: **a low-priority frame has no guaranteed delivery
time on a loaded bus.** When a CAN advert says *real-time*, it means a worst case somebody
calculated, not a fast average somebody measured.

**The payload is eight bytes with signals packed at bit offsets**, and the meaning lives outside
the protocol in a `.dbc` file. Signals are rarely byte-aligned — a 12-bit value starting at bit 20
is ordinary — and there are two conventions for bit order, Intel and Motorola, which the frame does
not distinguish. The demo decodes the same eight bytes both ways and gets 2758 rpm and 5685 rpm.

**If that feels familiar, it is chapter 2 again.** A 32-bit Modbus counter split across two
registers has exactly this problem: the protocol fixes the order inside a unit and says nothing
about the order between units, the documentation is a file somebody emails you, and the failure
mode is a plausible number rather than an exception.

---

## 5. Where .NET actually runs down here

The honest map, because adverts are vague and candidates guess:

| Target | What runs | Real? |
|---|---|---|
| Industrial PC / panel PC, Windows | Full .NET, WPF, the whole course | Overwhelmingly the most common |
| Windows IoT Enterprise | The same .NET, on a locked-down Windows edition | Common on OEM equipment |
| Linux on ARM (edge gateway, Raspberry Pi) | .NET on ARM64, `System.Device.Gpio`, Docker | Growing fast; advert C |
| .NET nanoFramework | A cut-down CLR on a microcontroller | Real, niche, rarely in adverts |
| A microcontroller | C/C++. Not .NET | Advert B — not your job |

**And the sentence that matters most: Windows is not a real-time operating system, and neither is
Linux by default.** You can hit a 100 ms deadline reliably. You cannot promise a 1 ms one, because
a garbage collection, a page fault or a driver can take the CPU away from you at any moment.
Hard timing belongs in the PLC or the microcontroller, which is precisely why those exist.

That is not a weakness to hide in an interview. It is the correct architectural answer, and saying
it unprompted marks you as somebody who has thought about the boundary:

> *"Deterministic timing belongs below me — in the PLC or the drive. I take the tags, keep the
> record and answer to the ERP, and I design so that my worst case does not stop the line."*

---

## 6. Do this

1. **Run both demos.** Then write, from memory, the four steps of a correct read loop.
2. **Take three real adverts** that say *embedded* and classify each as A, B or C using the table
   in section 1. If you cannot classify one in thirty seconds, it is written badly enough that the
   question itself is worth asking at first contact.
3. **Rehearse the boundary sentence** in section 5 until it is one breath, not a hedge.
4. **Never claim firmware.** The same rule as PLC programming in [chapter 7](07-the-job.md), for
   the same reason: it is discovered in one follow-up question and it costs you everything else
   you said.

---

## 7. Golden rules

1. **A read is not a message.** `Read` returns whatever arrived — buffer across reads, scan for
   frames, and keep the remainder.
2. **Frame explicitly.** Start byte, end byte, length prefix or delimiter; the device chose one and
   your reader must implement it rather than assume timing.
3. **Discard bytes before the first start marker.** You joined a conversation already in progress.
4. **Verify the checksum, count the failures, and do not treat one as fatal.** The rate is the
   diagnostic, not the event.
5. **RS-232 is point to point; RS-485 is a party line.** *Seriale* in an advert does not say which,
   and the difference is the whole design.
6. **On RS-485, silence is part of the protocol.** Wait for the turnaround before the next request,
   or you collide with the answer to the last one.
7. **One port, one owner.** The unit of mutual exclusion is the request and its answer, not the
   call to `Write`.
8. **A CAN frame has no addresses.** The identifier names the message; everyone hears everything.
9. **On CAN the lower identifier wins, always.** A low-priority frame has no guaranteed delivery
   time on a loaded bus.
10. **Bit order is not in the frame.** Intel or Motorola is in the `.dbc`, and choosing wrong gives
    a plausible number rather than an error.
11. **Windows is not an RTOS.** Hard deadlines belong in the PLC or the microcontroller, and saying
    so is the correct answer rather than an admission.
12. **Classify the advert before you apply.** *Embedded* next to C#, Modbus and supervisione is
    your job; *embedded* next to C, FreeRTOS and STM32 is somebody else's.

---

## 8. Interview questions

**"You open a serial port and read. What do you get?"**
Whatever has arrived — which may be half a message, three messages, or nothing. The protocol has
framing and the stream does not, so I accumulate into a buffer that lives across reads, scan it for
complete frames, take those out and leave the remainder. The bug I am avoiding is the one-read-one-
message loop, which passes at a desk with one slow device and loses data the moment the device gets
busy.

**"The integration works here and fails at the customer. Where do you look first?"**
Timing and topology before code, if it is RS-485. On site there are more devices on the bus, a
longer cable and usually a faster poll loop, so the first suspects are the turnaround delay before
the next request, two owners of one port, and termination. The tell is intermittent checksum errors
rather than a crash — a code bug is reproducible and this one is not.

**"What is the difference between RS-232 and RS-485?"**
232 is point to point and full duplex; 485 is a multi-drop bus, half duplex, up to 32 devices on one
pair. The consequence is that 485 must be polled with exactly one talker at a time and enforced
silence between, which is why Modbus RTU looks the way it does.

**"How does CAN decide who transmits when two nodes start at once?"**
Bit-by-bit arbitration on the identifier. Each node reads the wire while it writes; a node that
sends a recessive bit and reads a dominant one has lost and stops immediately. The lowest identifier
wins, its frame is not corrupted, and nothing is retransmitted — which is why the identifier *is*
the priority, and why safety-critical messages get low numbers.

**"Same eight bytes of CAN payload, two different engine speeds. How?"**
Bit order. Signals are packed at bit offsets and there are two conventions, Intel and Motorola;
nothing in the frame says which, so the `.dbc` file is the only source of truth. It is the same
class of problem as a 32-bit counter split across two Modbus registers, and it has the same nasty
property: the wrong answer is a plausible number, not an exception.

**"Can you guarantee a 1 ms response in C# on Windows?"**
No, and I would not design something that needed me to. A GC pause, a page fault or a driver can
take the CPU at any time, so I can be reliable at tens of milliseconds and not below. Hard timing
belongs in the PLC or the microcontroller. What I own is everything above that: the tags, the
record, the dispatch and the ERP, designed so my worst case does not stop the line.

**"Have you written firmware?"**
No. I write the software that talks to it — serial framing, Modbus, CAN decoding, OPC UA — and I
have implemented those by hand to understand what I am being handed. Firmware on a microcontroller
in C is a different job and I would not claim it.

---

[← The job itself](07-the-job.md) · [Module 28](README.md)
