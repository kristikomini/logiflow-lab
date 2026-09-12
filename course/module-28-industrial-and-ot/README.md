# Module 28 — Industrial software and the IT/OT boundary

> Every other module in this course assumes your software serves people. This one is about
> software that serves machines — and in the towns this course was written for, that is where a
> very large share of the .NET work actually is. The stack is familiar: C#, SQL Server, WPF, a
> service that never stops. The assumptions are not.

[Module 17](../module-17-career-emilia-romagna/) ranks the skills this region hires for, and for
years its row on industrial context said *"not in this course"*. This module is that row's answer.

---

## MES, SCADA, WCS, OT — the words this module claims

You will meet these four in the first ten minutes of an interview, and using them precisely is
worth more than it should be, because almost nobody applying at junior or mid level can.

**OT — operational technology.** The half of the company that runs the machines, as opposed to
**IT**, which runs the computers. Separate network, separate budget, separate people, often a
separate reporting line, and — the part that surprises everyone — separate values. IT optimises
for confidentiality and patching. OT optimises for *availability* and for not touching a thing
that currently works. When somebody says "the OT team will never allow that", they are not being
obstructive; they are being measured on uptime you are about to risk.

**SCADA — supervisory control and data acquisition.** The screens in the control room and the
software behind them: live tag values, alarms, an operator pressing start. It answers *what is
happening right now*, its horizon is milliseconds to minutes, and it is usually a bought product
(Siemens WinCC, Movicon, Ignition) rather than something you write.

**MES — manufacturing execution system.** The layer between SCADA and the ERP. It answers *how is
this order being made*: dispatch a production order to a line, count good pieces and scrap, record
why the line stopped, compute OEE, and keep the traceability record. Its horizon is the shift and
the order. This is the row you are hired for.

**WCS — warehouse control system.** The same idea for material handling instead of production:
conveyors, sorters, stacker cranes, AGVs. It answers *which vehicle moves which load unit, by
which route, right now*, and it sits under a **WMS** (warehouse management system) the way an MES
sits under an ERP. Around Modena this is a specific and very live industry — automated warehouses
for food and beverage, and robotic fleets — and a WCS advert reads almost word for word like an
MES advert with different nouns.

**So: MES and WCS are the two shapes of the same job**, and both are ordinary C# and SQL Server
with one unusual edge. Everything below is that edge.

---

## Deeper chapters

| | Chapter | |
|---|---|---|
| 1 | [The boundary](01-the-boundary.md) | where your software stops and the machine begins, and why the port earns its cost |
| 2 | [Protocols on the wire](02-protocols-on-the-wire.md) | Modbus by hand, OPC UA by model, MQTT by topic — and what "we already have OPC UA" turns out to mean |
| 3 | [Telemetry and backpressure](03-telemetry-and-backpressure.md) | subscribe rather than poll, the two clocks, quality, and who waits when your consumer cannot keep up |
| 4 | [Traffic, and the deadlock](04-traffic-and-deadlock.md) | two vehicles and one aisle: prevention by construction, and why zone size is the throughput dial |
| 5 | [OEE and traceability](05-oee-and-traceability.md) | the number on the wall, the argument underneath it, and the recall query you must be able to answer |
| 6 | [OT security](06-ot-security.md) | the isolated network, the Purdue model, the HMI you may not patch, and how data actually leaves |
| 7 | [The job itself](07-the-job.md) | commissioning, `cantiere`, `trasferta`, FAT and SAT, and the 03:00 call |
| 8 | [The edge, and reading the advert](08-the-edge-and-the-advert.md) | serial framing, RS-485, CAN arbitration — and telling a .NET advert from a firmware one |

---

## 1. The pyramid, and the row you are hired for

The industry organises itself as **ISA-95**, and it is the shared vocabulary in any meeting about
factory software. [Site chapter 32](../../site/chapters/32-industrial-and-mes.html) draws it; the
short version is five levels, from the physical process at Level 0 up to the ERP at Level 4.

```
   Level 4   ERP            orders, planning, costing, invoicing          weeks and months
   ─────────────────────────────────────────────────────────────  B2MML / XML / REST
   Level 3   MES / WCS      dispatch, capture, OEE, traceability          the shift, the order
   ─────────────────────────────────────────────────────────────  OPC UA, MQTT, Modbus
   Level 2   SCADA / HMI    supervision, operator screens, alarms         seconds
   Level 1   PLC            sensors and actuators, the scan cycle         milliseconds
   Level 0   the process    the press, the kiln, the filling line
                                                                  ↑
                                          OT below this line, IT above it,
                                          and you are the software that crosses it
```

Say "Level 3" in an interview and everyone in the room knows exactly what you mean.

**The useful thing about the diagram is not the levels; it is the two boundaries.**

- **Downward**, to Level 2/1, you are talking to something that has no schema, no transactions and
  no patience. It will not wait for you, it will not resend, and it does not know what an order is.
- **Upward**, to Level 4, you are talking to something that has all three and thinks in money.
  The ERP believes 500 units were produced; you recorded 496; somebody has to reconcile that, and
  it is you.

Every hard problem in this module lives on one of those two lines. The middle — C#, a database,
a service, a screen — is the part you already know.

---

## 2. What the machine layer gives you, and what it refuses to

The single most useful mental correction, if you are arriving from web work: **you are not
querying a system of record. You are receiving a stream of observations, most of which are already
slightly out of date, some of which are wrong, and none of which will be repeated.**

| You are used to | Down there |
|---|---|
| A request returns the current truth | A value is a *sample*, taken at a time that is not now |
| Missing data is an error | Missing data is Tuesday. A sensor drops out; the value is `Uncertain`, not zero |
| You can ask again | The 60 ms fault pulse is gone. There is no history to re-query |
| The server waits for you | The line does not slow down because your consumer is busy |
| A schema describes the payload | A register is a number at an address. The meaning is in a spreadsheet |
| Time is one clock | There are two, and the gap between them is the thing worth alarming on |

Run the demos rather than taking that table on trust:

```bash
cd labs/Labs.Playground
dotnet run modbus        # a real Modbus TCP server and client: the wire has no types
dotnet run tags          # polling misses a 60 ms fault; then who pays when you fall behind
dotnet run oee           # one shift, two definitions of "planned", eight points of difference
dotnet run traffic       # two AGVs, one aisle, and the one-line fix
dotnet run serial        # a byte stream has no messages, and the RS-485 party line
dotnet run can           # arbitration by identifier, and bits packed at an offset
```

And the last one, which needs the OPC Foundation stack and so lives outside the solution as a
file-based app:

```bash
dotnet run labs/opc-ua.cs
```

---

## 3. Why this software is written differently

**Downtime has a price per hour, and everybody knows what it is.** A website going down is
annoying and people come back later. A line going down costs thousands of euro an hour, and a
hundred people stand next to it while it does. That single fact explains most of the culture:
the reluctance to upgrade, the long acceptance tests, the preference for boring technology, and
why "we'll fix it forward" is not an argument that will land.

**The operator is not a user in the web sense.** They are wearing gloves, standing up, under
time pressure, possibly not reading the language your UI is in, and they cannot stop to think.
This is why the industrial world is still full of WinForms and WPF with enormous buttons and no
scrolling — and it is a genuine reason, not inertia. [Site chapter 31](../../site/chapters/31-desktop-wpf-winforms.html)
covers the desktop stack itself.

**The software outlives the fashion, and often the vendor.** Fifteen-year-old installations are
normal. Something you write will still be running when nobody who wrote it is at the company, on
a Windows version that is out of support, next to a machine whose manufacturer has been acquired
twice. Write for that reader.

**You cannot test in production, and production is the only realistic environment.** There is one
line, it is making product, and you may have it for four hours on a Sunday. This is why
*simulation* is a first-class engineering activity here rather than a nice-to-have — a simulator
you can run a thousand times is the only way to be confident before the four hours start. It is
also, conveniently, what an intralogistics interviewer means when they ask about testing against
a simulation rather than against the plant.

**Nobody times out.** In a web system a stuck request eventually fails and something retries. Two
AGVs nose to nose in an aisle will still be there in the morning. Deadlock is not an exception to
handle; it is a class of bug you must make impossible.

---

## 4. Do this

1. **Run the demos.** Then re-read the table in section 2 and check that each row is now a
   thing you have watched rather than a thing you have read.
2. **Read the register map argument in [chapter 2](02-protocols-on-the-wire.md)** and be able to
   say, out loud, what could go wrong with a 32-bit counter split across two 16-bit registers.
3. **Learn the four words** at the top of this module well enough to use them without hedging.
   Then learn the ten Italian ones in [chapter 7](07-the-job.md), which are the ones you will
   actually hear in the room.
4. **Decide where your boundary is, and say it.** "I am a .NET developer; I have not programmed a
   PLC and I would not claim to" is a sentence that makes everything else you claim credible. See
   [chapter 7](07-the-job.md).
5. **Ask the three questions** in [chapter 7](07-the-job.md) at your first interview with a
   machine-builder or an intralogistics company. How much `trasferta`? Who owns the PLC side? What
   happens at 03:00?

---

## 5. Golden rules

1. **You are hired for Level 3, and the job is the two boundaries.** Below is a machine with no
   schema and no patience; above is an ERP that thinks in money. The middle is ordinary C#.
2. **The machine layer has no schema.** A register is a number at an address, and units, scaling,
   word order and what counts as `running` all live in a spreadsheet outside the protocol.
3. **A value is a sample, not the truth.** It was taken at a time that is not now, and asking
   again gets you a different one rather than the same one confirmed.
4. **Never poll for events.** Subscribe. The interesting things on a line are shorter than any
   interval you can afford, and the ones that matter most are the shortest.
5. **Two timestamps, always** — when the machine says it was true, and when you received it. One
   column throws away the only latency measurement the protocol gives you for free.
6. **Store quality, never just the value.** `Bad` and `Uncertain` are readings, not nulls, and
   averaging them with good ones invents data that nobody measured.
7. **The machine does not wait for your consumer.** Backpressure is a choice between losing the
   past and losing the present, and it must be made deliberately rather than defaulted into.
8. **Store the events and compute the number.** A stored OEE cannot be explained, cannot be
   recomputed when the definition changes, and the definition will change.
9. **Whoever defines planned downtime sets the OEE.** The same shift is 84% or 76% depending on
   one decision that involves no machine at all.
10. **Performance above 100% is a data-quality alarm, not a good day.** The configured ideal cycle
    time is wrong, or somebody ran the line over its rated speed.
11. **Traceability is a legal obligation, not a feature.** In food, pharma and automotive the
    question is which lot, which machine, which shift — and the answer must survive years.
12. **Deadlock on a floor is prevented, not detected.** Acquire zones in a total order or grant a
    whole route atomically, because nothing times out when two vehicles are nose to nose.
13. **Zone size is the throughput dial**, and tuning it beats clever code — coarse zones serialise
    moves that never actually conflict.
14. **The line is on an isolated network on purpose.** Data leaves OT through a gateway, outward
    only, and "we'll just put it in the cloud" is a proposal, not a plan.
15. **Assume you may not patch the HMI.** A Windows box from 2009 whose vendor warranty forbids
    you to touch it is normal; you compensate around it rather than fixing it.
16. **Commissioning is the job, not the end of it.** Half of this work happens on site, with the
    line stopped and people waiting, and the candidate who knows that is the one who lasts.
17. **A read is not a message.** `SerialPort.Read` returns whatever arrived — buffer across
    reads, scan for frames, and carry the remainder into the next read.
18. **RS-232 is point to point; RS-485 is a party line.** *Seriale* in an advert does not say
    which, and on RS-485 the silence after a request is part of the protocol.
19. **A CAN frame has no addresses, and the lower identifier always wins.** The id names the
    message and sets its priority, so a low-priority frame has no guaranteed delivery time.
20. **Bit order is not in the frame.** Intel or Motorola lives in a `.dbc` file, and choosing
    wrong yields a plausible number rather than an error — the split Modbus counter again.
21. **Windows is not an RTOS.** Hard deadlines belong in the PLC or the microcontroller, and
    saying so is the correct architectural answer rather than an admission.
22. **Classify the advert before you apply.** *Embedded* beside C#, Modbus and supervisione is
    your job; *embedded* beside C, FreeRTOS and STM32 is somebody else's.

---

## 6. Interview questions

**"What is an MES, and how is it different from SCADA and from an ERP?"**
SCADA is what is happening right now — tags, alarms, operator screens, a horizon of seconds. MES
is how this order is being made — dispatch, production counts, stop reasons, OEE, traceability, a
horizon of a shift. The ERP is what we should make and what it cost, over weeks. If SCADA stops
the line is blind; if the MES stops you lose the record rather than the product; if the ERP stops
nobody on the floor notices today.

**"How would you get data out of a PLC?"**
Ask what is already there before proposing anything, because the answer is usually decided. OPC UA
if the installation is modern — a subscription, not a poll, with an information model that carries
type, unit, quality and a source timestamp. MQTT for newer IIoT sensors and for pushing outward.
A direct driver — Siemens S7, Modbus TCP, EtherNet/IP — for older or cheaper equipment, where you
will be handed a register map as a spreadsheet. And in practice also: a barcode scanner that
behaves as a keyboard, an operator choosing a stop reason from a list, and a CSV dropped on a
share every ten minutes by a machine from 2004.

**"Why is subscription better than polling here?"**
Because you cannot poll fast enough. A fault that lasts 60 ms is invisible at 1 Hz, and the events
that matter most are the shortest. Polling also costs the device: asking 5,000 tags every second
is real load on a PLC that has a scan cycle to meet. Subscription inverts it — the server samples
at an interval you agree, and publishes only what changed.

**"The ERP says 500 units were produced; the MES says 496. Who is right?"**
Neither, until you know what each is counting. The usual causes are a boundary — pieces made after
the ERP's cut-off time, or scrap counted at a different station — and a definition, such as whether
a reworked piece counts once or twice. The answer is not to make the numbers agree; it is to be
able to show, from the event log, exactly which four pieces differ and why. Which is only possible
if you stored events rather than a total.

**"Your consumer cannot keep up with the tag stream. What do you do?"**
Decide what to lose, explicitly. Blocking the producer is correct for a work queue and wrong for a
machine — the line will not slow down, so you have converted data loss into an unbounded lag and a
buffer overflowing somewhere you cannot see. Usually: drop the oldest for the live screen, so it
stays current, and put anything you are legally required to keep down a separate durable path.
Then fix the consumer, because both of those are triage.

**"Two AGVs are stopped facing each other in an aisle. What went wrong, and how do you prevent it?"**
Each holds a zone the other needs, so there is a cycle in the wait-for graph. It cannot be
recovered by timeout, because nothing on a floor times out — they will still be there tomorrow.
Prevent it by construction: a total order on zone ids so every vehicle acquires ascending, or an
allocator that grants a whole route atomically. If you choose atomic grants, add FIFO queueing,
or you have traded deadlock for starvation.

**"Why can't we just send the line data straight to the cloud?"**
Because the OT network is deliberately isolated and the people who own it are measured on uptime.
The route out is a gateway at the boundary that pushes outward and accepts nothing inward, with a
store-and-forward buffer for when the link drops. It is usually achievable — but as a proposal to
the OT team with an answer for what happens when the link is down, not as an architecture diagram
that assumes it.

**"Have you programmed a PLC?"**
If you have not: say so plainly, then say what you *do* own. "No — I am a .NET developer. I work
above the PLC: I take the tags, keep the record, dispatch the work and answer to the ERP, and I
have written a Modbus client and an OPC UA client to understand what I am being handed." That
answer is respected. Claiming ladder logic you have not written is discovered in one follow-up
question, and it costs you the rest of the interview.

---

## Next

→ [The Laws of C#](../LAWS-OF-CSHARP.md) — the whole language and runtime as one numbered canon.

This is the last module, and deliberately the least like the others: it is the one whose value is
mostly vocabulary and context rather than mechanism. That is not a weakness of the material. It is
the shape of the advantage — the machine market's technical bar for a .NET developer is the bar
this course has already cleared, and what stops most candidates is that they cannot hold the
conversation.
