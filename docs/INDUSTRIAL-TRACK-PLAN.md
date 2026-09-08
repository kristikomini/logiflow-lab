# Industrial, OT and robotics — a plan to make this repo hireable in the Modena machine market

**Audited 2026-09-08.** Five job postings across Modena / Reggio / Bologna, plus the shape of the
Indeed and Glassdoor result sets behind them. Sources at the bottom; verify before you quote any
of it in a cover letter.

The question this answers: *what "robotics / engineering" skills do employers around Modena
actually ask a **software** candidate for, and what is the cheapest honest way to put them in this
repository?*

---

## 1. The evidence

| # | Employer / place | Role | Requirements, as written |
|---|---|---|---|
| A | **System Logistics S.p.A.** (Krones Group), Fiorano Modenese — ~430 people, intralogistics for food & beverage | **PC Software Engineer WCS** | *"Progettare, sviluppare e installare soluzioni software per l'automazione di magazzini automatici, con focus su **supervisione e gestione del traffico**"* · *"software per il **tracking** e la gestione del traffico dei sistemi intralogistici"* · coordinate with the software, electrical and PM teams *"durante l'intero ciclo di vita del progetto"* · *"partecipare attivamente alle **attività di cantiere** fino al rilascio al cliente"* · bug-fixing *"durante il **commissioning**"*. Skills: **EWM, HMI, APS, Tracking, T-SQL, SQL Server, OOP, Microsoft .NET, C, Windows Form, WPF, Web API, applicazioni backend**, SW Quality, **SVN/Git**, Agile, Scrum, DevOps. Laurea tecnica. |
| B | **E80 Group** (ex Elettric80), Viano (RE) — AGV/LGV robotic logistics | **.NET Senior Software Engineer — AGV traffic management** / **PC Software Developer** | The `SM.I.LE80` platform does *real-time monitoring and control of robots and autonomous vehicles (AGV/LGV)*, *bidirectional communication with the customer ERP*, and *warehouse/plant throughput optimisation*. Work is customising `SDM`, `LgvManager` and the GUI, **testing in simulation environments**, then troubleshooting during customer installations. |
| C | Modena-area SME (unnamed in the aggregator) | C# developer, automation | *"Sviluppo di nuove applicazioni desktop e cross-platform in C#"* with **OPC-UA** concepts and **Siemens PLC**; maintenance of existing **Delphi + SQL** software; *"interfacciandosi direttamente con logiche e sistemi del mondo automazione"*. |
| D | **Pulsar Industry**, Spilamberto (MO); a junior post near Sassuolo; **Infomotion** | PLC/HMI developer · junior PLC programmer · C#/.NET programmer | PLC + HMI + SCADA (**Siemens TIA Portal**, Movicon, Weintek, OMRON), *"gestione dei test e dell'avviamento"*; the junior post lists **C# and SQL Server as preferential**; Infomotion writes *"software su misura per macchine e impianti industriali"* with **trasferte** abroad for testing and start-up. |
| E | Modena | **Vision systems in robotics & AI software engineer** | Laurea in automation/CS, industrial vision experience, **Python, C++, MATLAB, OpenCV, TensorFlow, PyTorch**. |

Two cross-cutting notes from the same sweep: recruiters describe the scarce profile as **hybrid
IT/OT** — someone who can stand between PLC/SCADA and the IT layer (cloud, data, security) — and
IIoT postings name **MQTT, OPC-UA, UDP and Azure** as the collection stack.

---

## 2. What the evidence actually says

**Posting E is not your job.** Machine vision and robot AI in this region is a Python/C++/MATLAB
role, usually filled from an automation-engineering degree. Chasing it with a .NET CV is how you
lose six months. Say so in the course rather than quietly omitting it — a reader deciding *not* to
apply for the wrong role is a real service.

**Postings A–D are one job wearing four hats,** and it is a job you are two-thirds qualified for
already. Strip the vocabulary away and every one of them wants: C# and .NET, SQL Server and T-SQL,
a desktop or web UI, a backend service that never stops, and the ability to talk to something that
is not a database. The *only* parts this course does not already teach are the last clause and the
working conditions around it.

So the honest framing — and this is the sentence the whole plan hangs on:

> **The robotics job available to a .NET developer in Modena is the IT/OT bridge.** Not kinematics,
> not ladder logic, not path planning. The machine is somebody else's problem; the software above
> it — that dispatches to it, records what it did, keeps the line moving and answers to the ERP —
> is yours.

And the piece of luck this repository has: **LogiFlow is already a warehouse and order-fulfilment
system.** Push it one layer down — give it conveyors, cranes, transport orders and a traffic
supervisor — and it stops being a clean-architecture demo and becomes **a WCS**, which is literally
the product System Logistics and E80 Group sell. No other course a Modena hiring manager sees will
have that.

---

## 3. Where the repo stands today

| | State |
|---|---|
| `site/chapters/32-industrial-and-mes.html` | **Exists and is good** — 25 KB, 7 sections: the ISA-95 pyramid, what an MES does, how the data gets in (OPC UA / MQTT / S7 / Modbus / barcodes / a CSV from 2004), why the software is written differently, a sketchable data model, golden rules, interview questions. Vocabulary-complete. |
| `site/assets/quizzes-3.js` | 7 questions on it. |
| `site/chapters/31-desktop-wpf-winforms.html` | Mentions barcode guns, cameras, PLC driver DLLs — one sentence. |
| `course/` — the deep half | **Was nothing at all.** Now [module 28](../course/module-28-industrial-and-ot/), added by wave 3 below. |
| `course/module-17-career-emilia-romagna/README.md:110` | Ranked *"Industrial context"* eighth of nine, and its "where you get it" cell read **"Not in this course."** Rewritten by wave 3 to point at module 28. |
| `src/`, `labs/` | **Was nothing.** Now five demos (wave 1) and a machine layer plus a WCS worker (wave 2). |

Which makes this **the only topic in the repository you can talk about but cannot run.** Everywhere
else the spine holds: a claim in prose, a demo in `Labs.Playground`, code in `src/` that the prose
cites by file and line. Here the spine is missing, and a reader can feel it.

---

## 4. The work, in five waves

Waves 1 and 2 are the ones that create something nobody else has. Waves 3–5 are the repo's normal
absorption machinery and are mechanical once the code exists.

### Wave 1 — the wire, runnable and offline · **DONE 2026-09-08**

All five demos are written and run. `labs/Labs.Playground/Demos.Industrial.cs` holds four of
them; the fifth is `labs/opc-ua.cs`. `dotnet build` and `dotnet run tools/doctor.cs` are clean —
except for four deliberate `labs/demos` warnings, which say that no module cites the new demos
yet. That is wave 3's job, and the warnings are the reminder. Do not silence them.

**What the work settled, that the plan could only guess at:**

- **§6 decision 1 is resolved: OPC UA is real, and it is a file-based app.** The OPC Foundation
  stack restores and runs entirely offline — it creates its own self-signed certificate into a
  temp PKI directory on first run, silently, with no prompt. So the fear in §6 was unfounded and
  the demo is genuine rather than mocked. It still lives outside the solution, for the reason
  that survived: it is four packages and a certificate store, and `Labs.Playground` is otherwise
  instant and dependency-free.
- **A file-based app inherits central package management and then cannot use it.** `#:package`
  with a version fails with `NU1008` under this repo's `Directory.Packages.props`. `opc-ua.cs`
  opts itself out with `#:property ManagePackageVersionsCentrally=false` and pins the exact
  versions in its own header — deliberately, so two packages nothing in the solution uses do not
  appear in the central manifest as though something did.
- **`RestorePackagesWithLockFile` is repo-wide, so restoring it produced `labs/packages.lock.json`.**
  It is the repo's own policy applied to a file-based app, and it pins the OPC UA graph. Commit it.
- **A wave-3 trap, found early.** `doctor.cs` parses citations with `dotnet run ([a-z][a-z0-9-]*)`,
  so the moment a module writes `dotnet run labs/opc-ua.cs` the check reads the demo name as
  `labs` and fails. The fix is one word: add `"labs"` to the `notDemos` set in
  `CheckDemoCitations`, exactly where `"tools"` already sits for the same reason.
- **The `Demo` record friction was real, and the fix was ten lines.** `Program.cs` now has an
  `Async(Func<Task>)` helper that adapts an async demo to the registry's `Action` shape. It
  blocks, which this repository teaches you not to do — so it carries a comment explaining the
  one condition under which it is correct (a console app has no `SynchronizationContext`).

**Two places the demos were made honest rather than impressive**, both worth keeping in mind
when the chapters get written on top of them:

- The backpressure comparison originally showed nothing, because `Task.Delay` on Windows has
  ~15 ms granularity and a "2 ms producer" and a "6 ms consumer" run at identical speed. It is
  now a burst, and it reports *which* ids survived rather than how many — `DropOldest` ends on
  `#59`, `DropWrite` ends on `#15`, same loss count, opposite windows.
- The OPC UA demo prints a **zero** gap between source and server timestamp, and says why: the
  server and the machine are one process, so there is nothing for the value to be late by. An
  earlier draft backdated the source timestamp to manufacture a gap. Faking the number in a
  chapter about why that number matters was not worth it.

#### As originally planned

New file `labs/Labs.Playground/Demos.Industrial.cs`, five entries appended to the `Demos` array in
`Program.cs` under a new `"industrial"` category:

| Demo | What it proves |
|---|---|
| `modbus` | **A Modbus TCP server and client, hand-written over `TcpListener`, no package.** ~150 lines: the MBAP header, function code 3, big-endian registers, a 32-bit value split across two 16-bit registers. This is the demo that earns the chapter — it turns *"you will meet raw register maps where the documentation is a spreadsheet"* from a warning into an experience. It is also exactly the repo's existing habit of building the thing by hand (module 08 builds a mediator). |
| `opcua` | The same tag read via **OPC UA**, against an in-process server, to show what an *information model* buys you over a register number: the tag knows it is a temperature in °C belonging to a machine. *Shipped as `dotnet run labs/opc-ua.cs`, and it grew a fifth section the plan did not anticipate: the certificate. Mutual X.509 trust is where every real OPC UA project actually starts, and "the first connection always fails until someone moves a file from `rejected` to `trusted`" is the most useful sentence in the demo.* |
| `tags` | **Subscription, not polling.** A change-driven tag feed into a `System.Threading.Channels` pipeline, with a deliberate slow consumer so **backpressure** is visible. The machine does not slow down because your consumer did; this is where a real line supervisor loses data, and it gives module 04 and module 21 an industrial home. |
| `oee` | **OEE = Availability × Performance × Quality**, computed from a raw event stream (starts, stops, stop reasons, good pieces, scrap) rather than stated as a formula. Includes the two arguments every plant has: does a planned changeover count against availability, and who owns micro-stops. |
| `traffic` | **Two AGVs want the same aisle.** Zone allocation, a deadlock produced on purpose, then the fix. This is *"supervisione e gestione del traffico"* from posting A and *"AGV traffic management"* from posting B, and it is the only genuinely algorithmic thing on the list. |

> **Friction to expect.** `labs/Labs.Playground/Program.cs:24` declares
> `record Demo(string Name, string Category, string Description, Action Run)` — synchronous.
> `modbus`, `tags` and `traffic` are all async. Either add a `Func<Task>` overload to the registry
> (cleanest, ~10 lines) or block at the edge. Decide once, in wave 1, before writing five demos
> around the wrong shape.

### Wave 2 — give LogiFlow a machine layer · **DONE 2026-09-08**

Built as planned, and it runs. `dotnet run` in `src/LogiFlow.Wcs` starts a simulated warehouse —
four vehicles, twelve zones, no hardware — and the heartbeat shows work moving. The full suite is
green at **264 tests** (was 234), architecture tests included, so the Domain layer stayed
dependency-free. `dotnet run tools/doctor.cs` is green on all thirteen checks.

| | |
|---|---|
| Domain | `Automation/` — `Equipment` and its five-state machine, `TransportOrder`, `TelemetrySample`, `ZoneAllocator`, errors and events |
| Application | `Abstractions/Automation/IEquipmentGateway.cs` — the port, plus `EquipmentDescriptor`, `EquipmentCommand`, `CommandAck` |
| Infrastructure | `Automation/` — `SimulatedPlcGateway`, a real `ModbusEquipmentGateway`, options, and the capability check in DI |
| Worker | `src/LogiFlow.Wcs/` — `TransportDispatcher`, `SimulationDriver`, `Program.cs` |
| Tests | 26 new: `EquipmentTests`, `ZoneAllocatorTests`, `TransportOrderTests`, and four `CommissioningTests` |

**All three promised payoffs landed.** Source and receive timestamps are two fields with the gap
exposed as `TelemetrySample.Age`; the state machine refuses `Faulted → Running` without an
acknowledgement, with a test; and the commissioning tests run **an hour of warehouse operation in
under a second**, asserting throughput, zone exclusivity, no leaked zones, and that faults cancel
their transport orders rather than inventing completions.

**Two bugs the work found, and one non-bug it nearly enshrined:**

- **The drop counter counted nothing.** `TryWrite` on a `DropOldest` channel returns `true`
  whether or not it discarded a sample, so the obvious implementation looks like it counts and
  does not. The fix is the `itemDropped` callback on `Channel.CreateBounded`, which is the only
  way to observe it.
- **A refused command stranded its zones forever**, and a queued route grant never reached the
  vehicle waiting for it. Both came from doing off-loop work — `SendAsync` is fire-and-forget by
  design — and both are fixed by queueing the outcome and draining it on the single loop.
- **The non-bug.** A live run appeared to stall at 13 completed, so a `ZoneAllocator.ServeQueue`
  was added to drain the queue every pass. Running for 75 seconds instead of 22 showed the floor
  recovering on its own (13 → 27 → 54 → 67): it was the thirty-second technician window, not a
  deadlock. The fix and its test were **removed**. Keeping defensive machinery for a failure that
  cannot happen is how a codebase accumulates code nobody dares delete.

**What was deliberately not built, and why:** no persistence. The fleet, the orders and the zone
table live in memory. That is honest for a demonstration, wrong for a plant — and worth knowing
that a database is the *easy* half of the restart problem. The hard half is
`ZoneAllocator.RebuildFromFloor`: after a crash the vehicles are physically where they are, and an
allocator that comes back from a table it wrote before it died will route a second vehicle into an
occupied aisle. That method exists and is tested; the storage behind it is wave 2b.

#### As originally planned

The point is not size, it is the **boundary**. One port, two adapters, and the same argument the
root `README.md` already makes about `IDistributedCache`: the application depends on an interface,
not on a product, and the repository proves it by shipping with the real one switched off.

```
Domain          Equipment (conveyor · diverter · stacker crane · AGV)
                EquipmentState  Idle → Running → Blocked → Starved → Faulted
                TransportOrder  move load unit X from location A to B
                TelemetrySample (tag, value, quality, source timestamp)

Application     IEquipmentGateway   ← the port. Subscribe, command, acknowledge.
                DispatchTransportOrder / RecordTelemetry / RaiseAlarm   (CQRS, as everywhere)

Infrastructure  SimulatedPlcGateway  ← default. Deterministic, tick-driven, no hardware.
                ModbusGateway        ← talks to the wave-1 server.
                OpcUaGateway         ← optional, config-gated, off by default.
```

Three things fall out of this that are worth more than the code:

- **Source timestamp vs receive timestamp.** The PLC has no clock you trust and the network has
  jitter. Every telemetry row carries both, and the difference is a first-class field. This is
  already a quiz question in `quizzes-3.js`; now it is a column.
- **The state machine rejects illegal transitions,** and a test asserts it. `Faulted → Running`
  without an acknowledged alarm is a bug that costs money on a real line.
- **A commissioning test.** Run the simulator for a simulated hour with `TimeProvider`, assert
  throughput and assert no deadlock. This is posting B's *"testing in simulation environments"*,
  and it is the single most quotable thing on the finished CV.

Placement question in §6: a `LogiFlow.Wcs` worker service beside `LogiFlow.Api`, or a hosted
service inside the API. Prefer the worker — a WCS that dies when the web app recycles is the
failure the chapter is about.

### Wave 3 — `course/module-28-industrial-and-ot/` · **DONE 2026-09-08**

Written as planned: a README and all seven chapters, ~16,000 words, each chapter ending in its own
rules list and every one of the five wave-1 demos now cited by name. `dotnet run tools/doctor.cs`
is green on all thirteen checks **with no warnings** — the four `labs/demos` warnings wave 1 left
as a reminder are gone, which was the point.

**Done alongside it, because wave 3 made the repo wrong without them** — these are wave-5 items
that could not wait, not scope creep:

- **The Golden rules chain, all three steps.** The card in the module README → hand-synced into
  `course/GOLDEN-RULES.md` → `dotnet run tools/viva-deck.cs`. Deck 362 → 378.
- **`doctor.cs` gained `"labs"` in `notDemos`**, exactly as wave 1 predicted. Without it the first
  `dotnet run labs/opc-ua.cs` citation fails the build.
- **Module 17 §3 row 8 no longer says "Not in this course."** It points at module 28 and site
  chapter 32, and says out loud that its eighth-place ranking understates it along the
  Sassuolo–Fiorano–Viano belt, where it outranks Azure.
- **Module 27's "Next" now points at module 28** rather than jumping to `LAWS-OF-CSHARP.md`, and
  module 28 ends by handing over to it. The chain is unbroken.
- **`course/README.md` gained the module 28 row** in Part VI.
- **Nineteen stale counts across ten files**, all found by `docs/counts` rather than by reading:
  the module total 28 → 29, deeper chapters 50 → 57, Golden rules 362 → 378. Two of the nineteen
  were spelled out as English words rather than digits — one in `course/GOLDEN-RULES.md` and its
  twin in module 17 — and a grep for digits would have missed both. The check reads words.

**One judgement call worth recording.** The plan said row 8 of module 17 §3 should *move up* the
ranking. It stayed at position 8 with a rewritten cell instead, because module 17's own closing
paragraph refers to "§3's fifth row" by ordinal — so reordering the table silently breaks a
cross-reference that `doctor.cs` does not check. The ranking argument is made in the cell's prose
instead, which costs nothing and breaks nothing.

**Still outstanding from wave 5:** only the sixth employer archetype in module 17 §2 — the
intralogistics and machine-automation vendor, which currently hides inside archetypes 1 and 2 and
hires differently from both. The `italiano.js` entries landed with wave 4, where they belonged.

#### As originally planned

**New module 28, appended, not inserted.** `docs/CONTENT-BACKLOG.md` already records that
renumbering a module's sections is survivable only after grepping every inbound reference; there is
no reason to take that risk for shelf order.

| File | Purpose |
|---|---|
| `README.md` | The module preface and its Golden-rules card. Claims the words *MES*, *SCADA*, *WCS*, *OT* out loud, the way module 25 was made to claim *microservices*. |
| `01-the-boundary.md` | Where your software ends and the machine begins, why the port exists, and the honest cost of the abstraction. Anchored on the real `IEquipmentGateway`. |
| `02-protocols-on-the-wire.md` | Modbus by hand, OPC UA by model, MQTT by topic. When each is what you will actually find, and what "we already have OPC UA" turns out to mean in the room. |
| `03-telemetry-and-backpressure.md` | Subscription over polling, `Channel<T>`, dropping vs blocking vs buffering, and the two clocks. Cross-links module 04 and module 21. |
| `04-traffic-and-deadlock.md` | Zone allocation, the deadlock, the fix. The one chapter a WCS interviewer will enjoy. |
| `05-oee-and-traceability.md` | The formula from the event stream; traceability as a legal obligation in food, pharma and automotive rather than as a feature; what a recall query looks like in T-SQL. |
| `06-ot-security.md` | Why the line is on an isolated network, why "just put it in Azure" gets refused, the Purdue model, unidirectional gateways, and the fact that a 2009 HMI cannot be patched. Cross-links module 24. |
| `07-the-job.md` | Commissioning, `cantiere`, `trasferta`, FAT/SAT, the 03:00 call, and what *"attività di cantiere fino al rilascio al cliente"* (posting A, verbatim) means for your life. Nobody writes this down and every candidate wishes somebody had. |

### Wave 4 — site chapters · **DONE 2026-09-08**

Both chapters written, not just 32b: `32b-talking-to-machines.html` (the wire and the boundary)
and `32c-wcs-traffic-and-commissioning.html` (traffic, OEE, traceability, and the job itself).
**34 questions appended**, taking the bank 464 → 498, and the Italian panel landed here rather
than waiting for wave 5.

Verified in a browser rather than assumed: both pages render with the sidebar, the generated
table of contents and full styling; every asset returns 200; the quiz block reports *"16 questions
on this chapter"* and *"18 questions"*; the 🇮🇹 panel renders with its accents intact. The only
404 is `/api/health`, which is the accounts service being absent from a static `http.server` —
nothing to do with these pages.

**What the site's own checks forced, and it is the interesting part:**

- **`site/code-dissection` is a hard rule, not a style note.** Every `<pre><code>` block must be
  immediately followed by a `.dissect` box, and the check counts both. Seven code blocks, seven
  dissections, confirmed in the DOM. It is the single best constraint in this repository: it makes
  it impossible to paste code into a chapter without explaining it line by line.
- **Links into the source must be absolute GitHub URLs.** Relative `../../src/...` resolves on
  disk and 404s on every deployment, because `site/` is published alone. `doctor.cs` carries a
  comment about the eighty-three links that were once correct locally and broken in production.
- **One new chapter cost thirty-nine stale counts across nineteen files** — chapters 51 → 53,
  questions 464 → 498, "chapters the advert never mentions" 28 → 30, the Italian panel's own
  coverage 31 → 34, plus two spelled-out totals buried mid-sentence in chapters 08 and 36, and
  `Academy:ChapterCount` in the accounts
  service, whose leaderboard would otherwise have computed mastery against the wrong denominator.
  Every one was found by `docs/counts` and `site/chapter-count`. None would have been found by
  reading.
- **`quiz-ids.lock` had to be regenerated** with `dotnet run tools/doctor.cs --update`, which is
  the deliberate friction: appending questions is free, and the lock is what turns *reordering*
  them into a build failure instead of a silent re-pointing of somebody's review schedule.

**The Italian is the part worth re-reading.** Chapter 32 had no panel at all before this, so the
entries cover 32, 32b and 32c — and this is the vocabulary that is genuinely not derivable from an
English CS education: `il collaudo`, `la messa in servizio`, `il cantiere`, `la trasferta`,
`il fermo macchina`, `la commessa`. The closing sentence of 32c's panel is the one to have ready:
*"So che gran parte del lavoro vero è il collaudo e la messa in servizio in cantiere. Posso
chiedere quante settimane di trasferta sono previste in un anno?"*

#### As originally planned

- **`32b-talking-to-machines.html`** — the wire and the boundary. Mirrors course files 01–03,
  with the `modbus` and `tags` demos dissected. **~14 questions appended** to `quizzes-3.js`.
- **`32c-wcs-traffic-and-commissioning.html`** — traffic, OEE, traceability, going on site.
  Mirrors 04, 05, 07. **~14 questions appended.** *Optional second pass — see §7.*
- **Two small fixes in `32` while you are in there.** Add a §Where the jobs are with the four
  employers from §1 named as archetypes, and soften the line *"ISA-95 defines two handshakes: OPC
  UA … and B2MML"*: B2MML is MESA's XML implementation of the ISA-95 models, and OPC UA is an OPC
  Foundation standard with an ISA-95 companion spec. The teaching shape is right; the word
  "defines" is not, and this is a chapter whose whole value is sounding like you have been in the
  room.

**Append only.** `tools/quiz-ids.lock` hashes every stem against its positional id — appending is
free, reordering fails the build rather than silently re-pointing someone's spaced-repetition
history.

### Wave 5 — the connective tissue

The steps that are easy to forget, in the order `tools/doctor.cs` will catch them:

1. **Golden rules.** Write the card in `module-28-industrial-and-ot/README.md` → hand-sync
   `course/GOLDEN-RULES.md` → `dotnet run tools/viva-deck.cs`. Three steps, and `doctor.cs` fails
   loudly between them by design.
2. **`site/assets/chapters.js`** — register 32b (and 32c) with title and blurb, or `site/manifest`
   and `site/chapter-count` both fail.
3. **`site/assets/italiano.js`** — `say` + `keep` entries for the new chapters. **This is the
   highest-value hour in the whole plan** and it is the one nobody would think to prioritise,
   because the vocabulary is not derivable from an English CS education:

   > `la commessa` (the job/order — the unit everything is organised around) · `il cantiere`
   > (the customer site) · `la trasferta` (travel to it) · `il collaudo` (acceptance test, FAT/SAT)
   > · `la messa in servizio` / `l'avviamento` (commissioning, start-up) · `il fermo macchina`
   > (downtime) · `il quadro elettrico` (the cabinet) · `il nastro trasportatore` (conveyor) ·
   > `il trasloelevatore` (stacker crane) · `il magazzino automatico` (automated warehouse) ·
   > `l'UDC — unità di carico` (load unit) · `il bancale` (pallet, and the word actually used) ·
   > `il prelievo` (picking) · `il lotto` (batch) · `la tracciabilità` · `la ricetta` (recipe) ·
   > `il turno` (shift) · `il pezzo buono` / `lo scarto` (good piece / scrap) · `la supervisione`
   > (the SCADA layer) · `il pulsante a fungo` (E-stop) · `il capitolato` (the spec you are held to)

   Coverage 31 → 33 of 53.
4. **`course/module-17-career-emilia-romagna/README.md`.** Row 8 of §3 stops saying *"Not in this
   course"* and points at module 28 and 32b — and it should move up the ranking, because for the
   Sassuolo–Fiorano–Viano belt it outranks Azure. Add a **sixth employer archetype** to §2:
   *the intralogistics and machine-automation vendor* — System Logistics, E80, Modula, Ferretto,
   and the machine builders — because it is currently folded into archetypes 1 and 2 and it hires
   differently from both (project work, `commesse`, travel, on-site release).
5. **Counts.** `docs/counts` and `site/chapter-count` guard the prose totals across `README.md`,
   `site/README.md`, `docs/BLUEPRINT.md` and `course/README.md`, and every one of them moves:
   chapters 51 → 53, the question bank 464 → ~492, course modules 28 → 29, Playground demos
   30 → 35, viva cards 362 → ~370. `labs/demos` separately checks that every demo cited by name in
   prose exists — so do not name `modbus` in a chapter before it is in the registry.

   *(Written with the number kept away from the noun on purpose. `CheckProseCounts` scans all of
   `docs/`, and a target total written the natural way — the figure immediately followed by
   `chapters`, `questions`, `modules` or `demos` — is read as a stale claim about the repository
   and fails the build. It is right to. The figures above are targets, not current state; this
   paragraph is the receipt, because the next person will hit it too.)*
6. **`dotnet run tools/doctor.cs`** green on all thirteen checks. That is the definition of done.

---

## 5. What this deliberately does not do

Worth writing into the module preface, because the restraint is the credibility:

- **No PLC programming.** No ladder, no structured text, no TIA Portal. You are not going to
  out-programme an electrotechnical diploma who has done it for fifteen years, and pretending
  otherwise in an interview ends badly.
- **No machine vision, no robot kinematics, no ROS.** That is posting E, and it is a different
  career (see §2).
- **No hardware.** Everything runs offline on one machine with no PLC, no licence and no dongle —
  the same rule the rest of the repository lives by.
- **No claim of shop-floor experience.** The existing chapter's framing is right: this is
  *vocabulary and the software layer*, and the value is that almost nobody else applying has either.

---

## 6. Decisions I need from you

1. ~~**OPC UA — real dependency or simulated?**~~ **Settled by wave 1: real, in a file-based app.**
   The certificate fear was unfounded — it self-signs silently on first run — and nothing it does
   can break `dotnet build`, because it is not in the solution. See the wave-1 note above.
2. **Does `src/` change, or does this all live in `labs/`?** Wave 2 as written adds a project to the
   real application. That is what makes it a portfolio piece — and it also means the machine layer
   has to meet the same bar as the rest of `src/` (every non-obvious decision commented where it was
   made, tests, no dead code). *Recommendation:* yes, do it, but as `LogiFlow.Wcs` (a worker), not
   as additions scattered through `LogiFlow.Api`.
3. **32b only, or 32b + 32c?** 32b alone closes the "can't run it" gap. 32c is what makes an
   interviewer at System Logistics sit up. *Recommendation:* both, sequenced — 32b in the first
   pass, 32c after wave 2's code exists to cite.

---

## 7. Sequencing and effort

Honest estimates, in your working days, assuming the research above is not repeated.

| Wave | Effort | Payoff |
|---|---|---|
| 1 — five demos | **2–3 d** (`modbus` alone is half of it) | High. Runnable, offline, quotable, and it unblocks everything else. |
| 2 — LogiFlow machine layer | **3–5 d** | Highest, and the only irreplaceable one. This is the thing no other candidate has. |
| 3 — module 28, eight files | **3–4 d** | High. It is the half of the course that is currently empty. |
| 4 — 32b (+32c) + ~28 questions | **2–3 d** | Medium-high, and mechanical once 1–3 exist. |
| 5 — tissue: rules, Italian, module 17, counts, doctor | **1 d** | Disproportionate. The Italian hour is worth more than any single chapter. |

**If you have one weekend and nothing else:** demo `modbus`, demo `traffic`, the `italiano.js`
vocabulary block, and the module 17 §3 row-8 rewrite. That is roughly six hours and it converts
"I have read about industrial software" into "I wrote a Modbus server and a deadlock-free zone
allocator, and I can hold the conversation in Italian." Everything else is depth on top of a claim
you can already make.

---

## 8. The payload — what this buys you in the room

Sentences you can say afterwards and defend under follow-up:

- *"My project is a WMS with a WCS layer under it. The application depends on an
  `IEquipmentGateway`, and I ship a simulated PLC and a Modbus adapter behind it, so the whole
  thing runs on one laptop with no hardware."*
- *"I wrote the Modbus TCP framing by hand, so I know what a register map costs you — and why an
  OPC UA information model is worth the weight when you can get it."*
- *"I compute OEE from the event stream rather than storing the number, because the argument is
  always about what counts as planned downtime."*
- *"Two vehicles asking for the same aisle deadlock unless you allocate zones in a total order —
  here is the test that reproduces it."*
- *"Ho lavorato sulla supervisione: ordini di trasporto, tracciabilità dei lotti e gestione del
  traffico. So che gran parte del lavoro vero è il collaudo e la messa in servizio in cantiere."*

That last one is the whole plan compressed into one sentence, and it is the reason wave 5 matters
as much as wave 2.

---

## Sources

Job-market evidence gathered 2026-09-08. Aggregator pages change; re-check before relying on any
individual figure or advert.

- [PC Software Engineer WCS — System Logistics / Krones, Fiorano Modenese (cercolavoro.com)](https://www.cercolavoro.com/offerta-lavoro-pc-software-engineer-wcs-fiorano-modenese-mo-krones-551033557)
- [PC SW Engineer WCS — System Logistics S.p.A. (SimplyHired IT)](https://www.simplyhired.it/job/5jnku9UQ72A87_5tMU9YaxIFT25INyRpxDAZj1kdT_USBLUfZv_Cig)
- [Dot Net Senior Software Engineer — AGV traffic management, ELETTRIC80 S.p.A.](https://elettric80.intervieweb.it/jobs/rd_software_engineer__agv_traffic_management_204497/us/)
- [Software Programmer — ELETTRIC80 S.p.A.](https://elettric80.intervieweb.it/jobs/pc_software_developer_228647/us/)
- [Vision systems in robotics & AI software engineer, Modena (ModenaToday)](https://www.modenatoday.it/annunci/vision-systems-in-robotics-ai-software-engineer-6a36z.job)
- [Sviluppatore software — Modena (Indeed IT)](https://it.indeed.com/q-sviluppatore-software-l-modena,-emilia-romagna-offerte-lavoro.html)
- [PLC — Modena (Indeed IT)](https://it.indeed.com/q-plc-l-modena,-emilia-romagna-offerte-lavoro.html)
- [Ingegnere automazione — Modena (Indeed IT)](https://it.indeed.com/q-ingegnere-automazione-l-modena,-emilia-romagna-offerte-lavoro.html)
- [Software engineer — Modena (Glassdoor IT)](https://www.glassdoor.it/Lavoro/modena-software-engineer-lavori-SRCH_IL.0,6_IC2794785_KO7,24.htm)
- [Automazione industriale — Modena (Jooble IT)](https://it.jooble.org/lavoro-automazione-industriale/Modena)
- [SM.I.LE80 / AGV-LGV systems — E80 Group](https://smart-factory.elettric80.com/en/industry-4.0/automated-guided-vehicle)
- [Warehouse control system (Wikipedia)](https://en.wikipedia.org/wiki/Warehouse_control_system)
- [OPC Unified Architecture (Wikipedia)](https://en.wikipedia.org/wiki/OPC_Unified_Architecture)

**Two sources I could not open.** The two ELETTRIC80 advert pages and the ModenaToday advert return
403 to an automated fetch; their content above comes from search-result extracts, not from the pages
themselves. Open them in a browser before treating any detail as verbatim.
