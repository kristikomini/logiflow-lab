# Telemetry and backpressure

> Why polling loses the events that matter, why there are two clocks, why a reading has a quality
> and not just a value, and who pays when your consumer cannot keep up.

```bash
cd labs/Labs.Playground
dotnet run tags
```

This is the chapter where [module 04](../module-04-async/) and
[module 21](../module-21-threading-and-memory-model/) stop being about theory. `Channel<T>`,
backpressure and a bounded queue are not an exercise here; they are the difference between a
maintenance report that is right and one that is quietly missing an event.

---

## 1. Polling loses, and it loses the things you most wanted

The demo simulates three seconds of a machine, with a fault that lasts **60 ms**. A poller running
at 1 Hz — a generous rate for a supervisor watching a few hundred tags — sees this:

```
   polling at 1 Hz  : saw states [running, running, running]
   subscription     : saw 3 transitions —
                          0 ms  → running
                       1370 ms  → FAULTED
                       1430 ms  → running
```

The poller reports a perfect shift. Not an approximately-right shift: **the fault did not happen**,
as far as your database is concerned, and the maintenance report is missing an event rather than
being slightly off.

The instinct is to poll faster. It does not work, for two reasons that are worth being able to
state:

**You cannot poll fast enough, and the events that matter are the shortest.** A micro-stop cleared
in eight seconds, a sensor that glitches for 40 ms, a fault that latches and clears before anyone
notices — those are precisely the phenomena somebody is paying you to find. A 100 ms poll still
misses a 60 ms pulse, and now you are doing ten times the work.

**Polling costs the device, not you.** Reading a tag is not free at the other end: it is work for
the OPC UA server and, behind it, for a PLC with a scan cycle to meet. Asking 5,000 tags every
100 ms is how a supervisor sitting quietly at a desk degrades a production line. This is the part
web habits do not prepare you for — in web work, hammering your own database is a problem you can
see and fix; here the thing you are degrading belongs to someone else and shows up as *the machine
got slower* long before anyone suspects the software.

**So: subscribe.** Both real protocols are built for it — OPC UA monitored items, MQTT topics —
and the design consequence reaches all the way up: your gateway interface exposes a stream and
deliberately does not expose `GetCurrentState()`, because offering one invites exactly the design
this section is about ([chapter 1](01-the-boundary.md)).

**Where polling is still right:** slow, non-event data where you want a heartbeat rather than
edges — a daily counter, a setpoint that only a human changes, a "is the gateway alive" check.
The rule is not *never poll*; it is *never poll for events*.

---

## 2. Two clocks, and the column everybody drops

Every reading has two times attached to it, and they are different:

- **Source timestamp** — when the machine says the value was true. The PLC scan that read the
  thermocouple finished at some moment, and that moment is already in the past.
- **Server timestamp** — when the server or gateway had it in hand.

OPC UA carries both, in the protocol, for free. Almost every quick implementation stores one
column, and the column it stores is the server's — or worse, `DateTime.Now` at the moment the row
was inserted, which is a third clock nobody asked for.

**The gap between them is your latency, and it is the cheapest diagnostic you will ever have.** A
gap that is normally 30 ms and becomes 900 ms tells you a gateway is struggling, or the network is
saturated, or a device is being asked for more than it can serve — and it tells you *before* an
operator notices the screen lagging. Alarm on the gap growing and you look prescient. Drop the
column and you cannot even ask the question retrospectively.

Three consequences that follow, and each is an interview answer:

**Order by source time, not arrival time.** Samples do not necessarily arrive in the order they
happened, especially across a gateway that buffers and forwards after a dropped link. A chart drawn
in arrival order is wrong in exactly the situations you are investigating.

**The machine's clock is not your clock, and it may be badly wrong.** PLCs are often not on NTP.
A device whose clock is eleven minutes slow will produce source timestamps that look like a
mysterious delay, and one whose clock jumps at DST will produce an hour of readings that appear to
happen twice — see [module 23](../module-23-text-culture-serialization/), which is about exactly
this class of bug. Store both timestamps and you can *detect* the bad clock; store one and you
have baked it in.

**Store UTC and a `DateTimeOffset`.** A plant that runs across a DST change — every plant — will
otherwise give you an hour that is genuinely ambiguous, in a traceability record you may have to
defend to an auditor.

---

## 3. Quality is a value, and zero is not a synonym for it

`Good`, `Uncertain`, `Bad`. OPC UA carries a status code with every reading, and the meaning is
not "did the request work" — it is **"how much should you believe this number"**.

An `Uncertain` reading is a sensor saying *I do not know*. That is information. It is not zero, it
is not null, and it must never be quietly averaged with real readings — an hour of thermocouple
dropouts recorded as `0 °C` will drag a shift average down by a plausible amount, and nobody will
ever find it, because there is no error anywhere and the number simply looks a bit low.

So: **the quality is a column, not a filter.** Store it with the value. Exclude bad readings from
aggregates deliberately, in a query you can point at, rather than by never having recorded them.
And when a value is bad for a while, that gap is itself a finding: a thermocouple that goes
`Uncertain` for a few minutes every afternoon is a loose connection somebody should tighten, and
it is invisible if your schema had nowhere to put it.

Modbus, of course, has none of this. Which is the point of [chapter 2](02-protocols-on-the-wire.md):
where the protocol has no quality, your gateway is *inventing* one, and it should say so — a
reading that has simply not been refreshed in ten seconds is not `Good`, whatever the absence of
an error suggests. Staleness detection is your job when the protocol will not do it.

---

## 4. When you cannot keep up, and who pays

The line does not slow down because your consumer is busy. This is the sentence to have ready,
because it is the whole difference between a work queue and a machine feed.

The demo sends a burst of 60 tag changes — a pallet arrives and everything changes at once — into
a bounded channel of 16, with a consumer that needs ~15 ms each because it is writing to a
database. Three policies, same burst:

```
                 held the      delivered   ids that
                 machine for   to the DB   arrived
                 ───────────   ─────────   ────────────
   Wait             765 ms       60/60      #0 … #59
   DropOldest         0 ms       16/60      #44 … #59
   DropWrite          0 ms       16/60      #0 … #15
```

**Look at the last column, not the counts.** Both drop policies lost exactly the same *number* of
events and lost completely different events — and only one of those two mistakes is visible on an
operator's screen.

- **`Wait` loses nothing and charges the producer.** Correct for a work queue; wrong for a machine.
  You cannot actually make a conveyor wait 765 ms, so what you have really built is a lag behind
  reality that grows for as long as the burst lasts, and then a socket buffer overflowing somewhere
  you cannot see or measure. The loss still happens; you have only moved it somewhere with no
  counter on it.
- **`DropOldest` stays current.** It ends on `#59`, the newest. Right for a live screen, where a
  value from four seconds ago is worse than useless because somebody is making a decision from it.
- **`DropWrite` keeps the beginning.** It ends on `#15` and never learns what happened after. Almost
  never right for telemetry; exactly right for something you must not duplicate or reorder, like an
  alarm sequence.

**The real answer on a line is usually both paths at once.** `DropOldest` into the live view, so
the screen is never stale, and a separate durable path — a file, a queue, the outbox in
[module 06](../module-06-efcore/) — for the events you are legally required to still have in three
years. Choosing one policy for everything is the mistake; the screen and the record have opposite
requirements and should not share a queue.

**And count the drops.** A channel that silently discards is a channel that will discard in
production without anybody knowing. `DropOldest` throws data away with no exception, no log line
and no return value — the demo can only show the loss by looking at *which ids arrived*. Increment
a counter on every drop and put it on the dashboard, or you have built a system whose failure mode
is invisible by construction.

---

## 5. The shape that works

Roughly, and it is the same shape in every industrial codebase worth reading:

```
   gateway  ──►  Channel<TelemetrySample>  ──►  batching consumer  ──►  bulk insert
   (subscribe)   bounded, DropOldest            50–500 rows or 1 s        SqlBulkCopy
                 + a drop counter               whichever first
        │
        └────►  live projection (in memory)  ──►  the screen
```

Three notes on that:

**Batch the writes.** A row-at-a-time insert per sample is the single most common reason these
systems fall over. Group by count or by elapsed time, whichever comes first, and use a bulk path —
the difference is not incremental.

**Keep the live view in memory, not in the database.** "What is the current value of every tag" is
a dictionary, not a query. Making the screen read the last row of a table that is being written to
at a thousand rows a second is how you produce lock contention that looks like a machine problem.

**Decide retention before the first row.** Raw samples at full rate, for a few weeks. Aggregates —
minute averages, min, max, count, and the quality distribution — forever. Nobody will thank you for
proposing this and everybody will be relieved when the table is 40 GB instead of 4 TB and the
questions people actually ask are still answerable.

---

## Golden rules

1. **Never poll for events.** The events worth finding are shorter than any interval you can
   afford, and polling costs the device rather than you.
2. **Poll only for heartbeats and slow values.** The rule is not "never poll".
3. **Store both timestamps.** The gap between source and server time is a free latency measurement
   and the earliest warning you will get.
4. **Order by source time, not arrival time.** Buffered gateways deliver out of order exactly when
   you are investigating something.
5. **The machine's clock is not on NTP.** Store both times and you can detect that; store one and
   you have baked it in.
6. **Quality is a column, not a filter.** `Uncertain` is a reading. Recording it as zero corrupts
   an average in a way nobody will ever find.
7. **Where the protocol has no quality, you are inventing one** — so treat a value that has not
   refreshed as stale rather than good.
8. **The machine never waits for your consumer.** `FullMode.Wait` does not prevent loss; it moves
   the loss somewhere with no counter on it.
9. **Screen and record want opposite policies.** `DropOldest` for the live view, a durable path for
   the legal record, and never one queue for both.
10. **Count every drop.** A silent discard is a failure mode invisible by construction.
11. **Batch the inserts and keep the live view in memory.** Row-at-a-time and a `SELECT TOP 1` per
    tag are the two ways this falls over at scale.

---

[← Protocols on the wire](02-protocols-on-the-wire.md) · [Module 28](README.md) · [Traffic, and the deadlock →](04-traffic-and-deadlock.md)
