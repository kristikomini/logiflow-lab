# OEE and traceability

> The number on the wall and the argument underneath it. Then the query you must be able to answer
> in an hour when somebody finds something in a box.

```bash
cd labs/Labs.Playground
dotnet run oee
```

These are the two things an MES is actually judged on. One is a percentage a plant manager looks at
every morning; the other is a legal obligation. Both are, from your side, the same discipline:
**store the events, compute the answer.**

---

## 1. OEE, and why it is a definition rather than a measurement

**OEE = Availability × Performance × Quality.**

- **Availability** = run time ÷ planned production time. Did it run when we wanted it to?
- **Performance** = (ideal cycle time × total count) ÷ run time. Did it run at the speed it can?
- **Quality** = good count ÷ total count. Was what came out sellable?

Three fractions, multiplied. Every one of them is a ratio of two numbers that somebody had to
define, and that is the whole chapter.

The demo takes one eight-hour shift as an event log — a 20-minute changeover, a 12-minute belt jam,
a 30-minute break, 18 minutes starved by an upstream machine, a 7-minute label fault, 12,400 pieces
of which 12,090 were good — and computes OEE twice, changing exactly one decision:

```
   planned downtime EXCLUDED
      planned production time   430 min      run time  393 min
      Availability  91.4%   Performance  94.7%   Quality  97.5%
      →  OEE  84.3%

   planned downtime INCLUDED
      planned production time   480 min      run time  393 min
      Availability  81.9%   Performance  94.7%   Quality  97.5%
      →  OEE  75.6%
```

**Same shift. Same machine. Same pieces. Eight and a half points.** Nobody touched anything on the
floor.

Both are defensible, and they answer different questions: *how well did the equipment run when we
asked it to*, and *how much of the shift did we get out of it*. The standard OEE definition is the
first. The second, widened all the way to calendar time, has its own name — **TEEP**, total
effective equipment performance — and the distinction is worth knowing because it stops the
argument being about arithmetic.

**What matters for you** is that the plant manager sees one number every morning and does not know
which definition produced it. Whoever decides what counts as planned downtime moves the number by
several points without touching a machine, and that person is often, by default, you — because you
wrote the query. Do not accept that quietly. Get the definition written down, agreed and dated, and
keep it somewhere a person can read.

You will also be asked to change it. Usually about a year after the number has been on a wall,
when a new plant manager arrives with a definition from their last job. **You can only survive that
if you kept the events** — a stored OEE percentage cannot be recomputed, cannot be explained, and
cannot be restated under a new definition. A table of stops with reasons and durations can be
recomputed a hundred times.

---

## 2. The losses that hide, and where they hide

**Micro-stops are the big one.** A jam cleared by an operator in eight seconds is usually below the
PLC's logging threshold, so it never becomes a stop event. The time is still gone — so it comes out
of **Performance** instead of **Availability**, and the line looks *slow* rather than *stopped*.

That misattribution sends improvement projects at the wrong target for a year. Somebody investigates
why the line runs at 94% of rated speed, looks at the machine, the product, the temperature, and
finds nothing — because the answer is two hundred eight-second stops nobody recorded. **If
Performance is mysteriously low and nobody can explain it, look for micro-stops first.** It is a
genuinely useful thing to say in an interview, because it is the diagnosis rather than the formula.

**Performance above 100% is not a good day.** It means the configured ideal cycle time is wrong, or
somebody ran the line above its rated speed. Clamp it, and raise it as a data-quality alarm.
Displaying 104% quietly teaches everybody that the number is decorative.

**Speed losses and reduced-speed running.** A line deliberately slowed for a difficult product is
not a fault, but it is not the ideal cycle time either. If nothing records *why* it was slow, it is
indistinguishable from degradation.

**Starved and blocked are not the same as broken.** A machine standing idle because the one
upstream stopped is being punished for someone else's failure. Recording the *reason* — and
specifically distinguishing "starved" from "blocked" from "faulted" — is what turns OEE from a
scoreboard into something actionable. Without it, the best machine on the line has the worst number
and nobody knows why.

**And the reason code is a human's choice.** The single most valuable field in the whole database
is usually the stop reason picked from a list by an operator on a touchscreen with gloves on. Which
means: keep the list short, keep it in their language, put the common ones first, and never make it
a free-text box — you will get "jam", "Jam", "jammed", "jam again" and one entry that is a phone
number.

---

## 3. Traceability, which is not a feature

The question is: *a customer complains about this box. What was in it, and what else is affected?*

The answer has to include which production batch it came from, which raw-material lots went into
that batch, which machine ran it, at what process values, on which shift and — depending on the
industry — which operator. In food, pharma, cosmetics and automotive this is a legal obligation
with named regulations behind it, not a nice-to-have, and the deadline for answering is measured in
hours because the alternative is recalling everything.

This changes the schema in ways that feel wrong coming from business software:

**You are modelling a graph of consumption, not a set of transactions.** A batch consumes lots and
produces units; those units become lots for the next stage. The genuinely hard query is the
transitive one — *given this finished unit, every raw lot that reached it* — and it is a recursive
CTE, which is [module 07](../module-07-sql-and-transactions/) doing real work.

**Genealogy runs in both directions, and both get asked.** *Backwards*: this box is bad, what went
into it? *Forwards*: this raw lot was contaminated, where did it all go? The forward query is the
one that decides how much product you recall, and it is the one people forget to test — because
the backward one is the one that comes up in the demo.

**It is append-only, and it is retained for years.** Records are corrected by a new record with a
reason and an author, never by an `UPDATE`. Retention is set by regulation and by the product's
shelf life, and it is routinely five to ten years — which is a schema decision, an archiving
decision and a "will this still be readable" decision, all taken on day one.

**Who and when are part of the record.** Which shift, which operator, which supervisor authorised
the deviation. This is where the audit trail from [chapter 1](01-the-boundary.md) stops being
architectural tidiness and becomes the thing an inspector reads.

**And it must survive the network being down.** A line does not stop because the MES server is
unreachable, so local buffering with store-and-forward is not an optimisation — it is the
requirement. Which makes the whole thing a duplicate-and-ordering problem, and therefore
[module 25](../module-25-distributed-systems/): at-least-once delivery, idempotent consumers, and a
natural key that makes a replayed batch harmless.

---

## 4. What to build, concretely

A shape that is boring on purpose, because it has to be readable in eight years:

```
   production_order      what we were asked to make
   batch                 one execution of it on one line, with start and end
   batch_input           batch × material lot × quantity        ← the genealogy edge
   unit / pallet         what came out, with its own id
   stop_event            batch × start × duration × reason × who entered it
   process_sample        batch × tag × value × quality × source time     (the cheap path)
   shift                 who was on, and when
```

Four decisions inside that worth defending:

**`stop_event` stores a duration and a reason, never a computed availability.** Everything in
section 1 follows from this.

**`batch_input` is the whole traceability story.** If it is right, both directions of the genealogy
query are a recursive CTE away. If it is missing — because somebody decided the ERP already knows —
you cannot answer the question at all, and finding that out during a recall is the worst possible
time.

**`process_sample` is on a different path with a different lifetime** to everything else in that
list. Full rate for weeks; minute aggregates with min, max, mean, count and the quality
distribution, forever. See [chapter 3](03-telemetry-and-backpressure.md).

**Every table carries the source timestamp, not just an insert time.** For the same reason as
everywhere else in this module, and here with an auditor attached to it.

---

## Golden rules

1. **OEE = Availability × Performance × Quality**, and all three are ratios somebody had to define.
2. **Whoever defines planned downtime sets the OEE.** The same shift is 84% or 76% with nothing
   changed on the floor.
3. **Store the events, compute the number.** A stored percentage cannot be explained, recomputed,
   or restated when the definition changes — and it will change.
4. **Get the definition written down and dated.** By default the person who chose it is you,
   because you wrote the query.
5. **Micro-stops hide in Performance.** Unexplained low Performance means unlogged short stops
   until proven otherwise.
6. **Performance above 100% is a data-quality alarm**, not a good day.
7. **Record why, not just that.** Starved, blocked and faulted are three different facts, and
   without them the best machine on the line looks like the worst.
8. **The stop reason is chosen by a person.** Short list, their language, common ones first, never
   free text.
9. **Traceability is a legal obligation with an hours-long deadline**, and the forward query — where
   did this bad lot go — is the one that decides how much you recall.
10. **Genealogy is append-only and retained for years.** Corrections are new records with an author
    and a reason.
11. **The line does not stop because your server is down.** Buffer locally, forward later, and make
    the consumer idempotent.

---

[← Traffic, and the deadlock](04-traffic-and-deadlock.md) · [Module 28](README.md) · [OT security →](06-ot-security.md)
