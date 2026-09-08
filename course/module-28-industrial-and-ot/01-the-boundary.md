# The boundary

> Where your software stops and the machine begins. What the port buys you, what it costs, and
> the one argument this repository has already made once — in a place you have read.

The machine layer is the only dependency in this whole course that you genuinely cannot have on
your laptop. There is no LocalDB for a filling line. That single fact decides the architecture,
and it decides it in a direction you have already met.

---

## 1. The shape, and why it is not negotiable

```
   Application          IEquipmentGateway            ← the port. Subscribe, command, acknowledge.
                                │
              ┌─────────────────┴─────────────────┐
              │                                   │
   SimulatedPlcGateway                 ModbusEquipmentGateway
   deterministic, no hardware,         a real socket to a real device,
   the default                         used only when a host is configured
```

It is in this repository, and it runs:

📂 [`IEquipmentGateway.cs`](../../src/LogiFlow.Application/Abstractions/Automation/IEquipmentGateway.cs) — the port
📂 [`SimulatedPlcGateway.cs`](../../src/LogiFlow.Infrastructure/Automation/SimulatedPlcGateway.cs) — the warehouse that does not exist
📂 [`ModbusEquipmentGateway.cs`](../../src/LogiFlow.Infrastructure/Automation/ModbusEquipmentGateway.cs) — the one that opens a socket
📂 [`src/LogiFlow.Wcs/`](../../src/LogiFlow.Wcs/) — the worker that drives it

```bash
cd src/LogiFlow.Wcs
dotnet run
```

With no configuration at all that is a simulated warehouse — four vehicles, twelve zones, no PLC,
no licence, no network. Set `Automation:Modbus:Host` and the same dispatcher drives a real device,
because everything above the port is unchanged either way.

Every industrial .NET codebase converges on this, and the ones that did not converge on it are the
ones nobody can test. The reason is not purity. It is that **the alternative is a codebase whose
only test environment is a customer's factory during a four-hour Sunday window.**

This repository has already argued exactly this, about something you can see. From the root
[`README.md`](../../README.md):

> `ConnectionStrings:Redis` is empty, so `Infrastructure/DependencyInjection.cs` registers
> `AddDistributedMemoryCache`. Every line of cache-aside code runs unchanged. But read the name
> carefully: it implements `IDistributedCache` and is **not distributed** — it is per-process.

That is the same move. The application depends on an interface; a capability check at startup
decides which implementation is registered; the code above the interface does not change and does
not know. And the honesty in that paragraph — *it is not actually distributed, and that difference
will bite you when you scale out* — is the honesty this chapter needs too, because the simulator
is not actually a machine and the difference will bite you.

---

## 2. What the port must expose, and what it must not

The temptation is to make the gateway a thin wrapper over the protocol: `ReadRegister(int)`,
`WriteRegister(int, ushort)`. Do not. That is not an abstraction, it is Modbus with C# syntax, and
the day the customer's next line is OPC UA you will find that "register number" has leaked into
three hundred call sites.

**Expose the vocabulary of the machine, not the vocabulary of the wire.**

📂 [`src/LogiFlow.Application/Abstractions/Automation/IEquipmentGateway.cs`](../../src/LogiFlow.Application/Abstractions/Automation/IEquipmentGateway.cs)

```csharp
public interface IEquipmentGateway
{
    // Which adapter is live, logged at startup so nobody has to guess.
    string Description { get; }

    // The machines this gateway serves. Commissioning data, not discovery — Modbus has no
    // request meaning "what do you have". An OPC UA adapter would populate it by browsing.
    IReadOnlyList<EquipmentDescriptor> Fleet { get; }

    // Everything the equipment reports, as it changes. Note what is NOT here: a snapshot.
    IAsyncEnumerable<TelemetrySample> SubscribeAsync(CancellationToken ct);

    // Returns when the machine has ACCEPTED the instruction, not when the work is done.
    Task<CommandAck> SendAsync(EquipmentCommand command, CancellationToken ct);

    // Its own method, because it is the one operation an auditor asks about.
    Task<CommandAck> AcknowledgeAsync(EquipmentId equipmentId, string acknowledgedBy, CancellationToken ct);
}
```

Three decisions in that small interface are worth defending in an interview:

**`SubscribeAsync` returns a stream, not a snapshot.** There is no `GetCurrentState()`, because
offering one invites the caller to poll, and polling loses events —
[chapter 3](03-telemetry-and-backpressure.md) has the demo. If a caller needs "the current value",
that is a projection your own code maintains from the stream, not a round trip to the floor.

**`SendAsync` returns an acknowledgement, not a result.** A conveyor does not finish moving a
pallet inside your `await`. It accepts the instruction, and the outcome arrives later, on the
telemetry stream, as an observation like everything else. Modelling it as `Task<MoveResult>` is
the mistake that produces a supervisor which holds a request open for ninety seconds and falls
over the first time a pallet jams.

**Acknowledging an alarm is its own method.** Not because it is technically different, but because
it is the one operation an auditor will ask about: who cleared the fault, when, and on whose
authority. Give it its own seam and the audit trail writes itself.

---

## 3. The simulator is a first-class citizen, not a stub

The instinct from web work is that the fake implementation is scaffolding — something to delete
once the real one exists. Here it is the opposite: **the simulator outlives the project**, because
it is the only environment in which you can run the same scenario a thousand times.

A simulator earns its place when it can do three things a real line cannot:

- **Run deterministically.** Same seed, same sequence, same outcome. Drive it from `TimeProvider`
  rather than the wall clock and a simulated hour takes milliseconds, which is what makes a
  throughput assertion a *test* rather than an afternoon.
- **Produce the failures on demand.** A jam, a sensor that starts reporting `Uncertain`, a vehicle
  that stops answering, a clock that jumps. On the real line you wait years for some of these and
  you cannot ask for them.
- **Be wrong in a known way.** This is the part people skip. Write down what your simulator does
  *not* model. SimulatedPlcGateway says so in its own remarks, and the list is short and
  specific: no packet loss or reordering, no electrical noise or stuck-at values, no partial
  reads, and no clock drift on the device — so a sample age there is always exactly the configured
  scan lag, where a real fleet has one machine eleven minutes out. Every entry is a bug class the
  simulator will never catch, and knowing them is worth more than a simulator that quietly
  pretends to be complete.

That last point is why `dotnet run labs/opc-ua.cs` prints a **zero** gap between its two
timestamps and then says, in the output, that the gap is zero because the server and the machine
are the same process. A demo that faked a plausible 40 ms would have taught the concept and lied
about the tool.

The simulator in src/ makes the opposite choice, for the opposite reason: it *does* apply a 40 ms
scan lag, because there its whole job is to exercise the two-timestamp code path — and it names
that number as a constant with a comment rather than burying it. The rule is not "never model
latency"; it is that a tool must never claim a fidelity it does not have.

---

## 4. Where it sits in the architecture you already have

Nothing here is new. The gateway is an infrastructure adapter behind an application-owned
interface, which is [module 05](../module-05-clean-architecture/) unchanged, and the arrow points
inward for the reason it always did: the Application layer owns `IEquipmentGateway` because the
Application layer is what depends on it.

Two placements that are specific to this domain, though:

**The gateway does not belong in the web application.** A WCS that stops dispatching because IIS
recycled the app pool at 03:00 is a stopped line. This work belongs in a worker service with its
own lifetime — a `BackgroundService` in its own process, not a hosted service inside the API.
That is also the answer when an interviewer asks why you would not "just put it in the API".

**Telemetry is not persisted through your normal write path.** Domain events, `SaveChanges`, an
outbox — [module 06](../module-06-efcore/) — are the right machinery for *decisions*: a transport
order dispatched, an alarm acknowledged, a batch declared. They are the wrong machinery for
50 samples a second of a temperature nobody will read individually. Those go down a separate,
cheaper path with a different retention policy, and conflating the two is how a change-tracking
context ends up with a million entities in it.

---

## 5. The honest cost of the abstraction

Every interface has a bill, and pretending otherwise is how you lose the argument to a senior
engineer who has paid it.

- **A leaky vocabulary.** Some concepts genuinely do not port. OPC UA has a status code with
  dozens of values; Modbus has none at all. Your `TelemetrySample.Quality` will be a lossy
  projection of one and an invention for the other, and someone will eventually need the specific
  code. Plan the escape hatch — a `RawStatus` property nobody uses in ninety-nine cases — rather
  than pretending the abstraction is complete.
- **Two things to keep in step.** Every capability added to the interface must be added to the
  simulator, or the simulator quietly stops representing the system. This is real, ongoing work.
- **A tempting lie.** The simulator always behaves. Code written only against it acquires
  assumptions — that values arrive in order, that a command is always accepted, that the stream
  never gaps — which are false on every real installation. Make the simulator misbehave on
  purpose, or it will teach you the wrong thing very convincingly.

The counter-argument, and it wins: without the port there is no test, no demo, no laptop
development, and no way for a new colleague to run anything on their first day. Every one of those
costs is smaller than that.

---

## Golden rules

1. **The machine is the one dependency you cannot have locally.** That is what forces the port —
   not architectural taste.
2. **The port speaks the machine's vocabulary, not the wire's.** `ReadRegister(int)` is Modbus
   wearing a C# hat, and it leaks into every call site.
3. **Subscribe, do not offer a snapshot.** A `GetCurrentState()` on the interface is an invitation
   to poll, and polling loses events.
4. **A command returns an acknowledgement; the outcome arrives on the stream.** Nothing on a line
   finishes inside your `await`.
5. **The simulator is a deliverable, not scaffolding** — and its value is proportional to how
   honestly you have written down what it does not model.
6. **Run the machine layer in its own process.** A supervisor that dies with the web app is a
   stopped line at 03:00.
7. **Telemetry does not go through the domain write path.** Decisions get the outbox; samples get
   a cheaper path and their own retention.

---

[Module 28](README.md) · [Protocols on the wire →](02-protocols-on-the-wire.md)
