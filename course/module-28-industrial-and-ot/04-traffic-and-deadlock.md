# Traffic, and the deadlock

> Two vehicles, one aisle. Why nothing times out on a floor, the one-line fix, why zone size is
> the throughput dial, and the starvation you introduce while fixing the deadlock.

```bash
cd labs/Labs.Playground
dotnet run traffic
```

This is the chapter a WCS interviewer will enjoy, because it is the only genuinely algorithmic
part of the job and because it maps one-to-one onto something you already know: it is
[module 21](../module-21-threading-and-memory-model/)'s lock ordering, with the locks bolted to
the floor and the threads weighing 600 kg.

---

## 1. The problem, stated the way the industry states it

A **transport order** is *move this load unit from A to B*. To execute it, a vehicle must own every
zone on its route while it is in them — a zone being a stretch of aisle, a crossing, a lift, a
pick station — because a zone that fits one vehicle and contains two is a collision.

So the traffic manager is a resource allocator. Vehicles are the threads, zones are the locks, and
the analogy holds all the way down including the part everyone hopes it will not:

```
   V1 route 0→1 : takes zone 0 ... now wants zone 1
   V2 route 1→0 : takes zone 1 ... now wants zone 0
```

Both vehicles are behaving perfectly reasonably. Both are stopped forever.

---

## 2. Why "add a timeout" is not the answer here

In a web system a stuck request eventually fails, something retries, and the incident is a latency
spike on a graph. That reflex is wrong here, and saying so out loud is one of the fastest ways to
show you understand the domain:

**Nothing times out on a floor.** Two vehicles nose to nose in an aisle will still be nose to nose
in the morning. There is no supervisor process that kills them and no user who gives up and
refreshes.

**There is no safe recovery action.** Suppose you detect the cycle. Now what? Reversing a vehicle
means driving backwards along a route that was planned forwards, past a zone that may now be
occupied, possibly carrying a two-tonne pallet, in a space designed on the assumption that vehicles
go one way. "Release your zone and back up" is a physical manoeuvre with its own safety case, not
a `catch` block.

**The cost is not a slow page.** The line behind the blocked aisle backs up, product accumulates,
upstream machines fill their buffers and stop, and somebody phones you. Deadlock here is measured
in euro per hour and in people standing still.

So: **deadlock is prevented by construction, not detected and recovered.** The timeout in the demo
exists only so the demo terminates, and the demo says so.

---

## 3. The one-line fix

A cycle in the wait-for graph requires two holders acquiring in opposite orders. Remove the
possibility of opposite orders and you have removed the cycle:

**Acquire zones in a total order — sort the route before taking it.**

```
── 1. Acquire in the order the route needs ──
   V1 took zone 0
   V2 took zone 1
   V1 STUCK waiting for zone 1
   V2 STUCK waiting for zone 0
   completed: 0 of 2

── 2. The same two moves, lowest zone id first ──
   V1 route 0→1, took [0, 1] — through
   V2 route 1→0, took [0, 1] — through
   completed: 2 of 2
```

Same two moves, same two zones, same vehicles. The only difference is `route.Order()`.

There is a second benefit that is easy to miss and worth mentioning if you are asked: **V2 now
waits before it enters rather than halfway through.** A vehicle blocked at the mouth of an aisle
is in a place a human can walk past; a vehicle blocked in the middle of one is an obstruction.
Ordered acquisition makes the blocking happen in the safer location, for free.

**The alternative is atomic route allocation:** ask for the whole route at once and get all of it
or none of it. This is what most real traffic managers do, because it also lets the allocator
reason about the route as a unit — reserving in the direction of travel, releasing zones behind
the vehicle as it clears them, and refusing a route that would trap another vehicle in a dead end.
Both approaches are correct; total ordering is simpler and composes badly with dynamic routes,
atomic allocation is more flexible and needs a real allocator.

---

## 4. The bug you introduce while fixing the first one

All-or-nothing allocation with retry trades deadlock for **starvation**. A vehicle asks for
`[3, 7]`, cannot have it, releases, waits, and asks again — and meanwhile other vehicles keep
slipping through with shorter routes. Nothing is stuck, throughput looks fine on the dashboard, and
one unlucky vehicle sits in the same spot for twenty minutes.

This is worse than deadlock in one specific way: **deadlock is obvious and starvation is not.**
Nobody raises a ticket, the aggregate numbers are healthy, and it is discovered because a shift
supervisor mentions that "that one always seems to be waiting".

The fix is **FIFO queueing on the zones**: a request that has been waiting takes precedence, and
later arrivals queue behind it rather than overtaking. In lock terms, a fair lock. It costs a
little throughput and it is not optional, and the fact that it costs throughput is why it is the
first thing removed by somebody optimising without knowing what it was for.

Two related failure modes worth naming, because an interviewer may reach for them:

- **Livelock** — two vehicles that both politely back off, both retry, and re-collide, forever. The
  cure is the same as everywhere else in this repository: randomised backoff, exactly the jitter
  argument from [module 25](../module-25-distributed-systems/).
- **The dead-end trap.** A vehicle routed into a spur whose only exit is then allocated to somebody
  else is not deadlocked in the wait-for sense — it holds nothing anybody wants — but it cannot
  leave. Prevention is a routing-time rule rather than an allocation-time one, and it is the reason
  real allocators reason about whole routes.

---

## 5. Zone size is the throughput dial

The third part of the demo runs the same fleet — six vehicles, four moves each, the same total
amount of driving — over the same floor cut two ways:

```
    3 zones   fleet finished in   621 ms
   12 zones   fleet finished in   369 ms
```

Same vehicles, same algorithm, no deadlock in either case. Coarse zones serialise moves that never
actually conflict: two vehicles at opposite ends of a long aisle are not in each other's way, but
if the whole aisle is one zone then one of them waits.

**This is what "throughput optimisation" means in a WCS job advert**, and it is mostly this
decision rather than clever code. Which is worth knowing before an interview, because the instinct
is to talk about algorithms and the honest answer is about modelling.

The dial does not go to infinity, and the counter-pressures are the interesting half:

- **Allocator traffic.** Every zone boundary is an acquire and a release. Cut finely enough and the
  vehicles spend their time asking permission.
- **More boundaries, more chances to hold two things at once** — which is more opportunity for the
  cycle you just designed out, if any part of the system is not ordering its acquisitions.
- **Physical reality sets a floor.** A zone smaller than a vehicle plus its braking distance is not
  a zone; the vehicle occupies two of them at all times and you have gained nothing but bookkeeping.
- **Safety zones are not yours to tune.** Some boundaries exist because a safety scanner or a light
  curtain is there, and those are fixed by a safety case that an engineer signed. You model around
  them.

---

## 6. What this looks like in code you would actually ship

The demo uses a `SemaphoreSlim` per zone because that is the smallest thing that shows the
behaviour. A real traffic manager differs in three ways, and knowing which three is the difference
between having read this chapter and having thought about it:

**It is a single-threaded allocator, not distributed locks.** One component owns the zone table and
processes requests in order. That is far easier to reason about, trivially fair, and fast enough —
you are allocating tens of zones per second, not millions. Reaching for distributed locking here
is a self-inflicted wound.

**Every allocation is journalled.** When the line stops, the question is *why did vehicle 7 wait
for four minutes*, and it is answered from a log of grants and releases with timestamps. Without
that journal the answer is a shrug, and you will be asked more than once.

**It survives a restart.** The vehicles are physically where they are, holding real space, whatever
your process thinks. On startup the allocator must rebuild its state from where the vehicles
actually report themselves to be — not from a table it wrote before it died. A traffic manager that
restarts with an empty zone table and starts granting is how two vehicles end up in one aisle with
the software convinced everything is fine, and that is the failure mode with a safety dimension
rather than an availability one.

---

## Golden rules

1. **A cycle needs two holders acquiring in opposite orders.** Sort the route and the cycle cannot
   form.
2. **Nothing times out on a floor.** Two vehicles nose to nose are still there in the morning, so
   deadlock must be prevented by construction rather than detected.
3. **There is no safe automatic recovery.** Reversing a loaded vehicle along a planned route is a
   physical manoeuvre with a safety case, not a `catch` block.
4. **Ordered acquisition also blocks vehicles in the safer place** — at the mouth of the aisle
   rather than in the middle of it.
5. **All-or-nothing plus retry trades deadlock for starvation**, which is worse because the
   dashboard looks healthy. Queue FIFO.
6. **Zone size is the throughput dial**, and tuning it beats clever code — but a zone smaller than
   a vehicle is bookkeeping, and safety boundaries are not yours to move.
7. **One single-threaded allocator beats distributed locks.** You are granting tens per second, not
   millions.
8. **Journal every grant and release.** "Why did vehicle 7 wait four minutes" is a question you
   will be asked, and the log is the only answer.
9. **Rebuild zone state from where the vehicles say they are**, never from a table written before
   the crash.

---

[← Telemetry and backpressure](03-telemetry-and-backpressure.md) · [Module 28](README.md) · [OEE and traceability →](05-oee-and-traceability.md)
