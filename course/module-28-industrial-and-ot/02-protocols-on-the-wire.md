# Protocols on the wire

> Modbus by hand, OPC UA by model, MQTT by topic. What you will actually find, why the oldest one
> refuses to die, and what "we already have OPC UA" turns out to mean once you are in the room.

Two demos underneath this chapter, and they are the point of it — reading about a register map is
not the same as watching the same eight bytes decode to two numbers a factor of twenty thousand
apart:

```bash
cd labs/Labs.Playground
dotnet run modbus
```

```bash
dotnet run labs/opc-ua.cs
```

---

## 1. Modbus, which is 1979 and is not going anywhere

Modbus is a number at an address. That is the entire data model. It was published in 1979 for
programmable controllers, it is free of licence and royalty, it fits in a microcontroller that
costs four euro, and it is consequently in an enormous amount of equipment that is still running.

The TCP framing is seven bytes of header and then a request:

```
   00 01   00 00   00 06   01   03   00 00   00 04
   ─┬───   ─┬───   ─┬───   ─┬   ─┬   ─┬───   ─┬───
    │       │       │       │    │    │        └ quantity: 4 registers
    │       │       │       │    │    └────────── starting address: 0
    │       │       │       │    └─────────────── function code 3, read holding registers
    │       │       │       └──────────────────── unit id
    │       │       └──────────────────────────── length: 6 bytes follow
    │       └──────────────────────────────────── protocol id, always 0
    └──────────────────────────────────────────── transaction id
```

You can implement a useful subset in an afternoon, which is exactly what the `modbus` demo does
over a raw `TcpListener`. Doing it by hand once is worth more than a library, because the library
hides the four things that will actually cost you a day each.

### The four traps, in the order you will meet them

**1. The documented number is not the address.** The spreadsheet says `40001`; the wire says
address `0`. The `4xxxx` prefix means "holding register" and the rest is one-based. Every Modbus
integration loses an afternoon to this once, and then never again.

**2. Word order between registers is undefined.** Modbus fixes the byte order *inside* a 16-bit
register and says nothing about the order *between* two of them. A 32-bit counter therefore has
two plausible readings, and the demo prints both from the same bytes:

```
   high-word-first :      100,003
   word-swapped    : 2,258,829,313   ← same bytes
```

There is no way to tell from the data which convention a device uses. You find out because the
number is absurd — or, far worse, because it is plausible. Verify against a value you can change
and watch, on the first day, before anything is built on top of it.

**3. There are no units, no scaling and no types.** A temperature arrives as `225` because
somebody decided to send tenths of a degree in a signed 16-bit register. Get the signedness wrong
and −1 °C reads as 65,526 tenths. Because the scaling lives outside the protocol, it must live in
exactly **one** place in your code — a tag definition — and never as an inline `/ 10.0` at a call
site, or the day the device is replaced with one that sends hundredths you will be hunting divisions
through a codebase.

**4. There is no schema and no discovery.** You cannot ask a Modbus device what it has. The answer
is an `.xls` attached to an email from an electrical engineer, it is the only copy, it is wrong in
one place, and it goes stale the first time somebody changes a machine without telling anybody.

What Modbus *does* have is an error path worth knowing, because it is one line of protocol and
people miss it: an exception response comes back with the function code's high bit set —
`0x03` becomes `0x83` — followed by a code, where `0x02` is illegal data address.

---

## 2. OPC UA, which is the spreadsheet moved into the protocol

Every trap above is an absence, and OPC UA is what filling those absences looks like. Run
`dotnet run labs/opc-ua.cs` — it starts a real server, connects a real client, and asks for the
same temperature. What comes back:

```
   name             Oven temperature
   description      Chamber temperature, thermocouple TC-1
   value            22.9
   type             Double
   status           Good [0x00000000]
   source timestamp 02:22:08.465   ← when the MACHINE says it was true
   server timestamp 02:22:08.465   ← when the SERVER saw it
```

Four things that were outside the protocol are now inside it: **a name and description**, **a real
type**, **a quality status**, and **two timestamps**. Plus a fifth that has no Modbus equivalent at
all — you can **browse**. `Line1.Oven` can be asked what it contains, and it answers.

Say this in an interview and it lands: *"OPC UA moves the spreadsheet into the protocol, and the
spreadsheet is what was going to go wrong."*

### The three things about OPC UA that are not in the marketing

**Security is certificates, and it is where every project actually starts.** Client and server each
hold an X.509 certificate and each must trust the other's before a session is permitted. The
practical consequence: **the first connection to a new server always fails.** The fix is a person
moving a `.der` file out of the `rejected` folder and into the `trusted` folder, on both machines,
usually over a phone call, usually at the worst moment. Knowing this in advance is worth an hour
of anyone's commissioning week.

The demo sets `SetAutoAcceptUntrustedCertificates(true)` so it can run at all. That means "trust
anybody who connects", which turns the entire authentication story into decoration. It belongs in
a commissioning tool and a test fixture, never in what you leave behind.

**Sampling and publishing are two different intervals, and confusing them is the classic mistake.**
Sampling is how often the *server* reads the device; publishing is how often it talks to *you*.
Sampling is server-side and device-side work — asking for 10 ms on 5,000 tags is how you bring a
PLC to its knees from the comfort of your desk — while publishing is network work. They are
separate on purpose, so a slow link cannot force you to miss fast events: the samples queue and
arrive together, each carrying the timestamp it actually had. Which only works if `QueueSize` is
greater than 1, and the default is 1.

**The deadband is the setting that decides your storage bill.** *Only tell me when it moves by
more than 0.5 °C.* On a real plant that is the difference between a database that fills in a week
and one that fills in a decade, and it is the first thing to ask about when somebody tells you
OPC UA is slow.

### Two things people get wrong about it out loud

- **"OPC UA" and "OPC Classic" are not the same thing.** Classic OPC (OPC DA) is COM/DCOM, Windows
  only, and configuring DCOM across a domain boundary is a rite of passage nobody enjoyed. OPC UA
  is a different protocol with a different stack — platform-independent, no COM. If somebody says
  "we have OPC", find out which, because the answer changes what you are being asked to do.
- **ISA-95 and OPC UA are different standards.** They are commonly named in the same breath, and
  there is an ISA-95 companion specification for OPC UA, but ISA-95 is the model of the levels and
  B2MML is MESA's XML implementation of it. Saying "ISA-95 defines OPC UA" is the kind of small
  wrongness that a person who has been in the room will notice.

---

## 3. MQTT, which is the one that leaves the building

MQTT is publish/subscribe over a broker: a client connects, publishes to a topic like
`plant/line1/oven/temperature`, and anybody subscribed to that topic — or to `plant/line1/#` —
receives it. It is small, it tolerates a bad link, and it is what newer IIoT sensors speak.

Three features that matter here rather than in a chat app:

- **The last will and testament.** A client registers a message with the broker when it connects,
  and the broker publishes it if the client disappears without saying goodbye. That is how a
  dashboard knows a machine went offline rather than went quiet, and it is a genuinely elegant
  answer to a problem that is awkward everywhere else.
- **Retained messages.** The broker keeps the last message on a topic and gives it to new
  subscribers immediately, so a screen that has just started does not sit blank until the next
  change. Without it, a subscribe-only design has no answer for "what is the value right now".
- **QoS 0, 1 and 2**, which is [module 25](../module-25-distributed-systems/) in miniature: at
  most once, at least once, exactly once. And the same conclusion applies — QoS 2 is expensive and
  narrower than it sounds, so the durable answer is still an idempotent consumer.

**Sparkplug B** is worth knowing by name: a specification layered on MQTT that fixes topic naming
and payload encoding, and adds birth/death certificates so a subscriber can tell a live value from
a stale one. Its whole purpose is to stop every integrator inventing a different topic tree, which
is exactly what happens without it.

---

## 4. Which one you will actually meet

You do not choose. This is the part that catches people who prepare by picking a favourite:
**the protocol is a property of the installation, and the installation is usually twenty years
old in places.** A single plant routinely has all of these at once.

| What you find | Why | What it means for you |
|---|---|---|
| **OPC UA** | modern line, or a gateway product already installed | Best case. Ask about deadbands and sampling before you ask about anything else |
| **Siemens S7 / Modbus / EtherNet/IP** | older or cheaper equipment, or a machine builder's own choice | You will be handed a register map as a spreadsheet. See section 1 |
| **MQTT** | newer sensors, or an existing push-to-cloud path | Ask who owns the broker and what the topic convention is. There is always a convention and it is never documented |
| **A shared folder of CSV files** | a machine from 2004 whose vendor no longer exists | Not elegant, entirely real, and it will outlive you. Treat the file as a protocol: partial writes, encoding, and the file that is still being written when you read it |
| **A barcode scanner** | humans are part of the system | It presents as a keyboard. The whole design question is which control had focus |
| **An operator typing it in** | some things are not instrumented | The stop reason is chosen from a list by a person, and it is the most valuable field in the database |

**The question to ask in the interview**, and it demonstrates more than any protocol knowledge:
*"What do the machines speak, and is there already a gateway?"* If there is a gateway, most of
this chapter is somebody else's problem and your job is the layer above it. If there is not, you
have just found out what the first three months are.

---

## Golden rules

1. **The documented register number and the wire address differ by one.** Always. `40001` is
   address 0.
2. **Word order between two registers is a guess** until you have verified it against a value you
   can change and watch.
3. **Scaling and units live in exactly one place in your code** — a tag definition — because they
   do not live in the protocol at all.
4. **Read the registers you need in one request.** A request per tag is how a supervisor that
   worked at your desk falls over on a real line.
5. **An OPC UA value carries name, type, quality and two timestamps.** That is the whole argument
   for it, and it is worth the weight whenever you can get it.
6. **The first OPC UA connection always fails on certificate trust.** Budget for it; never solve it
   in production by auto-accepting everything.
7. **Sampling interval and publishing interval are different**, and `QueueSize = 1` silently throws
   away everything between publishes.
8. **The deadband decides your storage bill**, and is the first question when somebody says OPC UA
   is slow.
9. **You do not choose the protocol; the installation does** — and a real plant has four of them
   at once, including a CSV on a share.

---

[← The boundary](01-the-boundary.md) · [Module 28](README.md) · [Telemetry and backpressure →](03-telemetry-and-backpressure.md)
