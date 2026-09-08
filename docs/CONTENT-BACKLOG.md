# Content backlog — gaps found against real job postings

Topics a real advert asked for that the course does **not** currently teach, or teaches only in
passing. Each entry says what is missing, why it matters for the Emilia-Romagna market, and where
it should go.

Add a new section each time a posting is audited. Strike an entry when the content lands.

## Status — 2026-09-05: all eleven gaps closed

Eight new chapters, 39 → **47**; question bank 312 → **421**. `dotnet run tools/doctor.cs` is green
on all ten checks.

| Gap | Chapter | n |
|---|---|---|
| 1 — SQL views and user-defined functions | `10b-views-functions-procedures` | 14 q |
| 2 — stored procedures beyond one example | same chapter | |
| 4 — consuming and receiving integrations | `14b-integrations` | 14 q |
| 7 — classic ASP.NET (Framework 4.x) | `16b-aspnet-framework` | 12 q |
| 10 — Framework → Core web migration | folded into `16b` §Moving it, as planned | |
| 8 — JavaScript as a language | `17b-javascript-language` | 15 q |
| 3 — microservices as an architecture | `22b-microservices` | 14 q |
| 6 — scheduled jobs | folded into `22b` §Scheduled work — the "two instances ran the same job" problem is a distributed-systems question, so it belongs there rather than in ch. 22 | |
| 11 — production support as a process | `25b-production-support` | 14 q |
| 9 — AWS | `29b-aws` (deliberately the shortest — the only *optional* skill in four adverts) | 12 q |
| 5 — AI | `33b-ai-in-dotnet` | 14 q |

### Correction — the course/ half was outstanding, now done

The status table above was written after building the **site** chapters only. Several gaps in this
document specified a `course/` mirror as well, and those were not done when the gaps were first
marked complete. They are now:

| Gap | course/ side |
|---|---|
| 1 + 2 | New deeper chapter `module-07-sql-and-transactions/07-views-functions-procedures.md`, plus README section 7 and 3 new card rules |
| 3 | New preface §Microservices — the word this module is underneath in `module-25-distributed-systems/README.md`, plus 2 card rules |
| 4 | New deeper chapter `module-25-distributed-systems/08-calling-someone-elses-api.md`, anchored on the real `LogiFlowApiClient` / `AccessTokenHandler` code in `src/LogiFlow.Web` |
| 7 | Module 17 §3 gains a ninth ranked skill and a second "unfair advantage" paragraph on legacy competence; `module-04-async/04-configureawait.md` now cross-links 16b where the deadlock is real rather than theoretical |

Notes for whoever does this next:

- **Renumbering a module README's sections is survivable but must be checked first.** Module 07's
  sections 7–9 were pushed to 8–10 to make room. Safe only because `grep` showed every inbound
  reference pointed at sections 1–6; `doctor.cs` (`course/sections`, `code/covered-in`) confirms.
- **Adding a rule to a module card is a three-step change**: the card, then `course/GOLDEN-RULES.md`
  by hand, then `dotnet run tools/viva-deck.cs`. `doctor.cs` fails loudly between steps — that is
  the design. Viva deck 352 → **362** cards.
- Module 25's contents-table numbers are file ordinals, not README section numbers (unlike module
  07's, which are section numbers). Check which convention a module uses before adding a row.
- `course/README.md` claimed "Fifteen of the modules … forty-five focused files". It was already
  stale before this work (48) and is now **sixteen / fifty**.

### Follow-ups

- **Italian panels — done.** All eight chapters have `say` + `keep` entries in
  `site/assets/italiano.js`, so the "🇮🇹 In italiano" box renders on each and they appear in the
  phrasebook's chapter-by-chapter list. Coverage 23 → **31 of 47**.
- **Labs demo — resolved, but not as AI.** Gap 5 said "a demo in `labs/Labs.Playground` if one can
  be written that runs offline". An offline *AI* demo is not worth writing: it would either need a
  network call or fake the only interesting part, and a faked non-deterministic model teaches
  nothing. **Closed as not-worth-doing, and replaced with `dotnet run retry`** — 240 clients
  retrying an outage, fixed backoff against jittered, drawn on one shared scale. Offline,
  deterministic (seeded), and it makes module 25 §4's jitter claim measurable instead of asserted.
  Cited from module 25 §4. Demo count 29 → **30**.
  - Writing it produced a worked example of why "run everything" is the course's one rule: the
    first version printed a *higher* peak for the jittered run and so argued against itself. Two
    causes — each histogram was normalised to its own maximum, and the 100 ms buckets were coarser
    than the 100 ms first delay, so every jittered first attempt piled into bucket zero. A
    synchronised retry is a spike, and a coarse ruler hides spikes.
- **Alerting section for ch. 25.** Gap 11 said the alerting material was the missing half of
  chapter 25. It is written, but it lives in `25b` §Alerting worth having rather than being folded
  back into 25 — worth revisiting if 25 is ever revised.
- The smaller notes below (temp tables, triggers, `MERGE`, key choice) are still open, and are all
  one-paragraph additions to existing chapters rather than new material.

**How new chapters are added** (learned the hard way, worth keeping):

- `site/assets/chapters.js` is the single source of truth — the sidebar, home-page cards, coverage
  tables, prev/next pager and the service-worker precache list all generate from that array.
  Adding an entry is the whole registration.
- **Never renumber an existing chapter.** Quiz ids are `<chapter-id>#<index>` and are the
  spaced-repetition keys, so a renamed or reordered chapter silently reassigns somebody's review
  history. New chapters therefore take a suffixed number — `10b`, `16b`, `17b` — inserted at the
  right position in the array. `n` is a display string; ordering comes from array position.
- Questions go at the **end** of a chapter's array in `quizzes-{1,2,3}.js`, split 00–12 / 13–25 /
  26–38 by chapter number.
- `site/assets/rules.js` is generated from `course/GOLDEN-RULES.md` and covers the *course*
  modules, not these chapters — a new site chapter needs nothing there.
- **Validate with `dotnet run tools/doctor.cs`**, then `--update` to re-lock the appended quiz ids.
  It already covers all of this and more; do not write a parallel checker. Two things it caught
  that a naive check does not:
  - its quiz-bank parser reads the option arrays textually, so a literal `[` or `]` inside an
    option string is reported as "fewer than two options". Write `1, 9, 10` in the option, and put
    the array in the question's `code` block.
  - `tools/quiz-ids.lock` hashes every stem against its positional id, so appending is free but
    reordering fails the build rather than silently re-pointing someone's review history.

One placement change from what was planned below: **16b sits after chapter 16, not after 01.**
The chapter explains Framework by contrast with Core — the event pipeline against middleware,
`web.config` against `IConfiguration`, `HttpContext.Current` against `IHttpContextAccessor` — and
none of those contrasts land before the reader has met the Core side in 14 and 16.

---

## Posting A — junior .NET / backend, audited 2026-09-04

> Sviluppo e manutenzione di applicazioni .NET/C# · API REST e servizi backend · integrazione tra
> sistemi web e piattaforme esterne · gestione DB SQL e SQL Server · query SQL di base · lettura,
> inserimento, aggiornamento ed estrazione dati · debug, test e correzione di anomalie ·
> collaborazione col team · progetti legati ad **AI, automazione, database, integrazioni e
> microservizi**. Requisiti: C#/.NET base, OOP, API e DB relazionali, SQL base, **SQL Server**,
> **modellazione dati e relazioni**, **stored procedure, viste o funzioni SQL**, **Git base**,
> progetti personali/stage/tesi, interesse per **microservizi, integrazioni web e soluzioni
> data-driven**.

This is a *junior* advert, and the course over-serves most of it. The gaps are narrow but real,
and three of them are named explicitly in the requirements list — which means they are screening
questions.

### Already covered — no action

| Requirement | Where |
|---|---|
| C# / .NET, OOP principles | site ch. 02–05; course modules 01–04, 26 |
| API REST, Web API, backend services | site ch. 13 (REST principles), 14 (Web API); course module 15 |
| Relational databases, SQL Server | site ch. 09 (tables, keys, types, relationships, NULL, normalisation, SQL Server specifics) |
| SQL base + CRUD (lettura/inserimento/aggiornamento/estrazione) | site ch. 10 (joins, aggregation, CTEs, window functions, paging **and writing data**); course module 07 |
| Data modelling and relations between tables | site ch. 09 §Relationships, §Normalisation |
| Debug, test, fixing anomalies | site ch. 24 (testing), ch. 27 (debugging — breakpoints, stepping, a method for finding bugs); course module 12 |
| Git basics | site ch. 26 (model, daily commands, branching, merge vs rebase, conflicts, undo, PRs) |
| Team collaboration, working from a ticket | site ch. 34 (agile), ch. 35 (analysis, code review, estimating) |
| Portfolio / personal projects / thesis | course module 17; site ch. 36 |
| Data-driven solutions | course modules 08, 09; site ch. 23 |

### Gap 1 — SQL views and user-defined functions · **missing entirely**

The advert says *"nozioni base di stored procedure, **viste o funzioni SQL**"*. Grepping
`course/` and `site/` for `CREATE VIEW`, `CREATE FUNCTION`, `table-valued`, `UDF`,
`user-defined` returns **nothing**. The only hits for "view" are MVC views and LINQ views.

Should cover:

- `CREATE VIEW` — what it is (a saved SELECT, not a saved result), when it earns its place
  (a stable contract over a messy schema, a permissions boundary), and when it does not.
- Updatable views, and why most are not.
- Indexed / materialised views on SQL Server — `WITH SCHEMABINDING`, and the cost on writes.
- Scalar UDFs and **why they are a performance trap** (row-by-row execution, plan opacity) —
  plus inlining in SQL Server 2019+.
- Inline table-valued functions as the version that is actually fine, and why.
- Mapping a view to an EF Core entity: `ToView(...)`, `HasNoKey()`.

Where: a new section in **site ch. 10** (next to §Parameters and stored procedures), or a new
chapter `10b`. Mirror as a deeper chapter under **course/module-07-sql-and-transactions/**.

### Gap 2 — stored procedures beyond one example · **thin**

One worked `CREATE OR ALTER PROCEDURE` exists in site ch. 10 §Parameters and stored procedures.
That is the whole treatment. A team that "expects stored procedures" (their words, ch. 00) will
ask more than that.

Missing:

- **Calling one from C#** — `SqlCommand` with `CommandType.StoredProcedure`, EF Core
  `FromSqlRaw("EXEC ...")` / `ExecuteSqlInterpolated`, and why a sproc that returns a shape EF
  cannot map needs `HasNoKey()` or Dapper.
- OUTPUT parameters and return values vs a result set.
- `SET NOCOUNT ON` — used in the existing example, never explained.
- Error handling inside T-SQL: `BEGIN TRY / BEGIN CATCH`, `THROW`, `XACT_ABORT`, and what happens
  to an open transaction when it fires.
- Parameter sniffing — the classic "it was fast yesterday" SQL Server interview question.
- The honest argument for and against putting logic in the database, in the style of the
  repository-pattern argument in course module 26.

Where: expand **site ch. 10**; deeper chapter under **course/module-07-sql-and-transactions/**.

### Gap 3 — microservices, as an architecture · **only mentioned in the negative**

Every occurrence of "microservice" in `course/` is CQRS module 08 saying *what CQRS is not*.
The *substance* is taught well — module 25 (dual writes, idempotency, retries, circuit breakers,
sagas), site ch. 22 (queues, brokers, delivery guarantees, outbox, dead letters) — but the word
the advert uses is never claimed, and the decomposition questions are never answered.

Missing:

- Monolith vs modular monolith vs microservices, and the honest cost of each. Where an Italian
  SME actually lands, and why — this is the module 17 angle.
- How you cut services — by bounded context, not by layer and not by table.
- Synchronous vs asynchronous inter-service communication, and how to choose.
- Service discovery, API gateway / BFF, per-service configuration.
- The distributed-transaction question, pointing at the existing saga chapter.
- What "we're moving to microservices" means in an interview, and the two questions to ask back.

Where: a new **site chapter** between 22 and 23, and a framing section at the top of
**course/module-25-distributed-systems/README.md** that claims the word.

### Gap 4 — integrating with external platforms · **scattered, no home**

*"Integrazione tra sistemi web e piattaforme esterne"* is a bullet in the responsibilities, and
"integrazioni web" is repeated in the nice-to-haves — so it is likely a large part of the actual
day job. Today the material is spread thin: `IHttpClientFactory` appears in module 10 as a DI
lifetime example, typed clients and delegating handlers in module 18 (Blazor), Polly in module 25.
There is no chapter on *consuming* someone else's API.

Missing:

- Typed `HttpClient` + `IHttpClientFactory` as a first-class topic rather than a DI footnote:
  socket exhaustion, handler lifetime, base address, default headers.
- Authenticating outbound: API keys, OAuth2 client credentials, token caching and refresh in a
  `DelegatingHandler`.
- Resilience on the outbound call — timeout, retry with jitter, circuit breaker
  (`Microsoft.Extensions.Http.Resilience`), and why a retry without idempotency is a duplicate
  order. Cross-link module 25 §4.
- Mapping and anti-corruption: never let their DTO into your domain.
- Handling *their* failures — partial responses, pagination, rate limits (`429`, `Retry-After`).
- **Receiving** integrations: webhook endpoints, signature verification, replay protection, and
  why the handler must be idempotent. `webhook` currently appears exactly once in the whole
  course, in the outbox chapter.
- File-based integration, which is what SME integration work often actually is: CSV/flat-file and
  XML exchange, SFTP drops, encoding and culture traps — cross-link module 23.
- Testing an integration without the third party: `WireMock.Net` or a stub handler, contract tests.

Where: a new **site chapter** after 14 (Web API), plus a deeper chapter under
**course/module-25-distributed-systems/**.

### Gap 5 — AI · **absent, and it is in the advert twice**

*"Partecipazione a progetti legati ad AI"* and *"interesse per l'intelligenza artificiale"*.
Searching both trees for `AI`, `LLM`, `OpenAI`, `Semantic Kernel`, `embedding`, `vector` finds
nothing. For a junior role this is an *interest* requirement, not a competence one — so the goal
is a short, honest chapter that lets you hold a five-minute conversation, not a course in ML.

Should cover:

- Calling an LLM from C#: `Microsoft.Extensions.AI` as the abstraction, one concrete provider,
  streaming, cancellation, timeouts — it is just an external HTTP integration, so it leans on Gap 4.
- Structured output — getting JSON back that deserialises, and validating it because one day it
  will not.
- Tokens, context windows and cost; why you cache and why you cap.
- RAG in one page: embeddings, a vector store, and SQL Server 2025's native `VECTOR` type or
  Azure AI Search — the version of this an Italian SME would actually build.
- Where AI belongs in the architecture: behind an interface, in Infrastructure, non-deterministic
  and therefore never inside a domain invariant. Testing it with a golden set, not assertions.
- The honest limits — hallucination, PII and GDPR, EU AI Act awareness. This is the part that
  makes you sound senior in an interview rather than credulous.

Where: a new **site chapter** near the end, before the CV chapters, and a demo in
`labs/Labs.Playground` if one can be written that runs offline.

### Gap 6 — automation and scheduled work · **partial**

`BackgroundService` is covered well (outbox dispatcher — modules 06, 10, 15). "Automazione" in the
advert probably also means scheduled jobs and internal tooling.

Missing: cron-style scheduling (Hangfire and Quartz.NET — neither appears anywhere), what happens
when two instances run the same job, distributed locks, and the "just use a `BackgroundService`"
answer together with its limits. Small — one section in **site ch. 22**, or an appendix to
course module 10.

### Smaller notes — all four done, 2026-09-05

- **Temp tables and table variables** — new §Temp tables and table variables in **site ch. 11**,
  built around the one-row cardinality estimate and the rollback difference.
- **SQL triggers** — new §Triggers, deliberately left out in **10b**, in the style of module 26's
  omitted patterns. Includes the EF Core consequence: a trigger on a table breaks EF's `OUTPUT`
  fast path until `HasTrigger` is declared, so a DBA can break an application nobody touched.
- **`MERGE`** — warning box in **site ch. 10** §writing data. The live issue is concurrency
  (no range lock without `HOLDLOCK`), not only the historical bug list.
- **Key choice** — ⚠ **the note below was wrong.** Site ch. 09 §Surrogate or natural key already
  covered surrogate vs natural, GUID page splits, `NEWSEQUENTIALID()` and the INT-clustered
  alternative. What was genuinely missing was `SEQUENCE` and the fact that `IDENTITY` guarantees
  uniqueness but not continuity — now added, with the point that Italian *numerazione progressiva
  delle fatture* must be gapless, so `IDENTITY` must never be a document number.

> Audit lesson: two of these four notes overstated the gap. Grep proves a term is *absent*; it does
> not prove the *idea* is missing under different words. Read the section before writing the entry.

---

## Posting B — skills list, audited 2026-09-04

> **Skill necessarie:** .NET · ASP.NET · ASP.NET Core · C# · JavaScript · Microsoft SQL Server
> **Skill facoltative:** AWS

A bare skills list rather than prose, so the *shape* is the information. Two details matter:
**ASP.NET is listed separately from ASP.NET Core**, and **JavaScript is a required skill sitting
next to C#**. Both are usually a description of a codebase, not a wish list.

### Already covered — no action

| Skill | Where |
|---|---|
| .NET | course module 00, 27; site ch. 01 (Framework vs Core vs 5+, SDK vs runtime, release cadence) |
| C# | course modules 01–04, 19–22, 27; site ch. 02–08 |
| ASP.NET Core | course module 15 (host, pipeline, two-phase routing, binding, Options, authn/authz, Kestrel); site ch. 14, 16 |
| Microsoft SQL Server | site ch. 09–11; course modules 06, 07 — **subject to Posting A gaps 1 and 2, still open** |

### Gap 7 — classic ASP.NET (Framework 4.x) · **named, but only as a disclaimer**

Listing "ASP.NET" *and* "ASP.NET Core" as two separate required skills almost always means the
company runs both: a legacy Framework 4.x web application still in maintenance, and newer work on
modern .NET. The course states the distinction correctly and then stops:

- `course/module-15-aspnetcore-in-depth/README.md` lines 28–30 — one callout box, "ASP.NET Core is
  not ASP.NET", naming `System.Web`, `Global.asax`, WebForms, IIS-only, Windows-only.
- `site/chapters/01-dotnet-platform.html` — a comparison-table row naming WebForms, MVC 5, Web API 2.
- `site/chapters/16-aspnet-mvc.html` is **ASP.NET Core MVC**, not MVC 5.

Nothing teaches you to open a Framework 4.x web app and be useful in it. Missing:

- The request pipeline that is *not* middleware: `Global.asax`, `HttpModule` and `HttpHandler`,
  and the page lifecycle. What `Application_Start` did that `Program.cs` does now.
- `web.config` — transforms, `<appSettings>`, `<connectionStrings>`, and why configuration is a
  different model from `IConfiguration`.
- WebForms in survival terms: what ViewState is, why postbacks exist, and how to read an `.aspx`
  with a code-behind without panicking. Not how to write new ones.
- MVC 5 and Web API 2 vs their Core equivalents — `System.Web.Mvc` vs `Microsoft.AspNetCore.Mvc`,
  the two different `Controller` base classes, `HttpContext.Current` and why it is a static that
  cannot exist in Core.
- **The async trap**: `ConfigureAwait(false)` and the classic deadlock matter *far* more on
  Framework, because `AspNetSynchronizationContext` is what makes them bite. Module 04 chapter 04
  already explains the mechanism — this needs the "and this is why the old app deadlocks" framing.
- Hosting on IIS: application pools, recycling, and what "the app is slow on first request after
  lunch" actually means.
- The migration conversation: incremental migration with YARP, `Microsoft.Extensions.*` on
  Framework, and the honest answer to "when is a rewrite worth it".

Where: a new **site chapter** immediately after ch. 01, while the Framework-vs-Core comparison is
still fresh. Interview framing belongs in course module 17 — being unbothered by legacy code is a
hiring advantage in Emilia-Romagna, where a lot of revenue still runs on Framework 4.x.

### Gap 8 — JavaScript · **thin for a required skill**

JavaScript is listed as *necessaria*, level with C# and SQL Server. Current coverage is
`site/chapters/17-frontend-basics.html` — HTML, the DOM, `fetch` against your own API, one table
titled "JavaScript oddities worth knowing" (7 rows: `===`, `const`/`let`, `null`/`undefined`,
falsy values, `?.`/`??`, Promises, IEEE-754), then TypeScript — plus ch. 18 on SPAs. Good material,
but it is *frontend orientation for a backend developer*, not the language.

Absent from both trees: `event loop`, `microtask`, `prototype`, `arrow function`, `destructuring`,
`ESM`/`CommonJS`, `jQuery`. Missing:

- **The event loop** — call stack, task queue, microtask queue, and why a `Promise` callback runs
  before a `setTimeout(0)`. This is *the* JavaScript interview question, and it maps directly onto
  course module 04 — single-threaded cooperative scheduling is the same idea `await` uses.
- `this`, and the four things it can bind to. Arrow functions as the fix, and why an arrow function
  is wrong as an object method.
- Closures in JavaScript, and the `for`-loop capture bug — the same bug as module 02, with a
  different fix (`let` vs `var`). Worth teaching as the same law twice.
- Prototypes and `class` sugar over them — enough to read a stack trace.
- Array methods as LINQ: `map`/`filter`/`reduce`/`find`/`some`/`every`, and the `reduce` that
  should have been a `for`. Cross-link site ch. 06.
- Destructuring, spread/rest, template literals, optional chaining — the syntax you cannot read
  a modern file without.
- Modules: ESM vs CommonJS, `import`/`export`, and what a bundler is doing.
- `async`/`await` in JavaScript, unhandled promise rejections, and `Promise.all` vs `allSettled` —
  the same shapes as module 04, so teach them as a translation table.
- DOM events: bubbling, delegation, `preventDefault`, and why delegation is how you handle a
  table with 500 rows.
- **jQuery** — currently zero mentions anywhere. If Gap 7 is right and there is a Framework 4.x
  app, its front end is jQuery. `$(document).ready`, selectors, `$.ajax`, and how to read it
  without learning it as a habit.

Where: split ch. 17 into "the browser and the DOM" and a new **"JavaScript, the language"**
chapter, written the way ch. 02–08 write C# — with the C# equivalent alongside each concept.

### Gap 9 — AWS · **absent (optional skill)**

Two real mentions in the entire repository: "AWS Secrets Manager" as an alternative in
`course/module-13-deployment/README.md` line 47, and OpenSearch described as an AWS fork of
Elasticsearch in site ch. 21. (Most grep hits for `AWS` are the file name `LAWS-OF-CSHARP.md`.)
Cloud coverage is Azure-only: site ch. 29 (Azure) and ch. 30 (Azure DevOps).

It is a *facoltativa*, so the target is credibility, not competence — enough to say "I have
deployed to it" and answer follow-ups:

- The five services a .NET service actually touches: ECS/Fargate or App Runner, RDS for SQL Server,
  S3, Secrets Manager or Parameter Store, CloudWatch.
- The AWS SDK for .NET, and `AWSSDK.Extensions.NETCore.Setup` for DI registration.
- IAM in one page — roles vs users, and why a task role beats an access key in an environment
  variable.
- Lambda with .NET: the custom runtime, cold starts, and why Native AOT matters there
  (cross-link module 22).
- The Azure ↔ AWS translation table, since ch. 29 already teaches the Azure half. That table is
  most of the work.

Where: an appendix to **site ch. 29**, or a short ch. 29b. Lower priority than gaps 1, 2, 5, 7
and 8 — it is the only optional skill in either posting.

### Ranking, both postings

1. **Gap 8 (JavaScript)** and **Gap 7 (classic ASP.NET)** — required skills in Posting B with
   near-zero coverage. Highest value.
2. **Gaps 1 and 2 (views, functions, stored procedures)** — named verbatim in Posting A, and
   SQL Server is required in both.
3. **Gap 5 (AI)** — named twice in Posting A, absent.
4. **Gap 4 (external integrations)** — largest by likely day-job volume.
5. **Gap 3 (microservices)** — substance exists, framing does not.
6. **Gaps 6 and 9 (scheduled jobs, AWS)** — smallest, and AWS is optional.

---

## Posting C — .NET developer, Microsoft stack, audited 2026-09-04

> Team di sviluppo in ambiente Microsoft · sviluppo e manutenzione di **applicazioni web .NET** ·
> collaborazione con team tecnici e **stakeholder** per attività evolutive e correttive ·
> **aggiornamento e migrazione tecnologica su framework .NET**. Mansioni: backend con **.NET Core
> e .NET Framework 4.8** · applicazioni web enterprise · gestione database SQL e scrittura query ·
> **ambienti cloud Azure e/o AWS** per deployment e supporto applicativo.
> Requisiti: 6 mesi–2 anni come .NET developer · .NET Core, **.NET Framework 4.8**, SQL ·
> gradita familiarità con **Azure e AWS**.

Adds no new *technology* beyond Postings A and B, but it promotes two existing gaps and adds one.

- **Confirms Gap 7 and raises it to first priority.** This advert does not merely list ASP.NET and
  ASP.NET Core side by side — it names **.NET Framework 4.8** by version, in the mansioni, as
  daily backend work. Three of the four postings audited so far imply a live Framework codebase.
- **Confirms Gap 9, and AWS is no longer optional.** "Ambienti cloud Azure e/o AWS" is a *mansione*
  here, not a nice-to-have. Azure is well covered (site ch. 29: services, resource groups, managed
  identity, App Service configuration, deployment slots, where to look when it breaks — which is
  exactly the "deployment e supporto applicativo" this asks for). The AWS half of that chapter does
  not exist.
- Stakeholder collaboration and "attività evolutive e correttive" — covered by site ch. 34, 35.
- SQL and writing queries — covered, subject to Gaps 1 and 2.

### Gap 10 — migrating a web app from .NET Framework to .NET · **desktop only**

*"Supporterai attività di aggiornamento e migrazione tecnologica su framework .NET"* is a listed
responsibility, and site ch. 00 already reads the `.NET / .NET Core` slash as "expect migration
work". The repository half-answers it: **`site/chapters/31-desktop-wpf-winforms.html` covers
.NET Upgrade Assistant and the strangler approach — for WinForms and WPF.** The web equivalent is
missing, and `YARP` appears once (module 15, as a reverse proxy) without the migration framing.

Missing:

- Assessing before moving: .NET Portability/Upgrade Assistant on a web project, and the things
  that genuinely do not port — `System.Web`, WebForms, WCF hosting, AppDomains. Site ch. 01 already
  names these; this needs the "so what do you do about it" half.
- `packages.config` → PackageReference, and old-style `.csproj` → SDK-style.
- The incremental route: YARP in front, route by route, both apps live at once. Why a big-bang
  rewrite is the answer that loses you the job.
- `netstandard2.0` as the bridge for shared libraries during the move.
- What breaks at runtime rather than at compile time: configuration (`web.config` →
  `IConfiguration`), the synchronisation context and `ConfigureAwait` (cross-link module 04),
  `HttpContext.Current`, and default culture/encoding differences (cross-link module 23).
- How to estimate and stage this work, and how to talk about it in an interview.

Where: fold into the new classic-ASP.NET chapter from Gap 7 as its closing section, so one chapter
covers "read the old app" and "move the old app". Cross-link ch. 31 for the desktop version.

---

## Posting D — .NET developer, eGlue platform, audited 2026-09-04

> Responsabilità: **analisi e documentazione** funzionale · **sviluppo e integrazione** su
> piattaforme eGlue con .NET · **qualità e testing** (performance e sicurezza) · **troubleshooting**
> e incident management / bug fixing · **monitoraggio** delle applicazioni in esercizio.
> Requisiti: laurea o diploma · 1–3 anni .NET · framework .NET / .NET Core · ottima conoscenza C#
> e OOP · buona conoscenza T-SQL e RDBMS (SQL Server) · principi REST e Web API · Visual Studio
> e/o VS Code · analisi e autonomia decisionale · team working e doti comunicative.

**The `Requisiti` list is, line for line and in the same order, the advert
`site/chapters/00-the-job-posting.html` already decodes.** Every one of the nine bullets has a row
in that chapter's table with a chapter pointer. Nothing to add — this is the posting the entire
`site/` tree was built around, so the requirements half of this application is fully served.

What is new is the **`Responsabilità` half**, which chapter 00 does not cover — it decodes
requirements, not duties.

| Responsibility | Status |
|---|---|
| Sviluppo e integrazione di sistema | **Gap 4** — the integration half has no home |
| Qualità e testing, performance e sicurezza | Covered — site ch. 24; course modules 12, 14, 24 |
| Monitoraggio delle applicazioni in esercizio | Mostly covered — site ch. 25, course module 11 (structured logging, correlation ids, health checks). **No alerting**: `alerting` returns no substantive hit in either observability file |
| Analisi e documentazione funzionale | **Partial** — see Gap 11 |
| Troubleshooting e incident management | **Partial** — see Gap 11 |

### Gap 11 — production support as a process: incidents, and the documents you write · **partial**

The *technical* skills are all there — debugging (site ch. 27), structured logging and correlation
(module 11), reading a plan (site ch. 11), the error-handling middleware (module 10). What is
missing is the **process** wrapped around them, which is what "incident management" and "analisi e
documentazione" name. `postmortem`, `triage`, `runbook` and `on-call` return zero hits across both
trees; `ADR` appears exactly once, in site ch. 35 §Deciding, and recording the decision.

Missing:

- The shape of an incident: detect → triage → mitigate → fix → write it up. Why mitigation comes
  before diagnosis, and why the rollback is usually the right first move.
- Severity levels, and how to decide one at 09:00 on a Monday without a rulebook.
- Alerting that is worth having: alert on symptoms not causes, on user-visible SLOs, and why an
  alert nobody acts on is worse than no alert. This is the missing section of site ch. 25.
- Reading production: correlation id → logs → trace, as a *drill* rather than as a concept.
  Module 11 has the mechanism; nothing walks it end to end under pressure.
- The blameless postmortem, and the five-whys that stops at "the deploy process allowed it"
  rather than at a person.
- **The documents a developer actually writes**, which is the "analisi e documentazione" bullet:
  a technical analysis from a business requirement, an ADR (expand the one paragraph in ch. 35),
  a release note, and a handover note. With one worked example of each, taken from this codebase.
- Bug reports and reproduction: the minimal repro as a professional habit, and why "works on my
  machine" is usually a configuration or culture difference (cross-link module 23).

Where: a new **site chapter** after 25 (Observability), titled for production support; plus the
alerting section folded into ch. 25 itself. The documentation half could equally extend ch. 35,
which already has the decision-recording paragraph to grow from.

### Research item — the eGlue platform

*"Sviluppare applicazioni sulle piattaforme eGlue"* names a specific vendor platform that appears
nowhere in this repository, and that I have not verified. Before applying, find out what it is —
whether it is a low-code/integration product with its own SDK, or a customer's in-house platform —
because it changes what "realizzare le relative integrazioni di sistema" means in practice, and it
is the obvious question to ask them. Do not guess at it in a cover letter.

---

## Standing summary across all four postings

Same band throughout: 6 months to 3 years, .NET backend, SQL Server, Italian SME. The course
covers the language, the architecture and the runtime far past what any of them ask. The gaps
cluster in three places, and they are the same three every time:

1. **The legacy half of the stack.** Gaps 7 and 10. Three of four postings imply a live
   .NET Framework 4.x/4.8 web application, and one names migration as a duty. The course
   correctly says "ASP.NET Core is not ASP.NET" and then only teaches the Core side.
2. **The edges of the service.** Gaps 4, 5, 9 and 11 — consuming and receiving external
   integrations, AI, AWS, and running the thing in production as a process. The course is
   strongest inside the service boundary and thinnest at every point where it touches something
   it does not own.
3. **The named-but-untaught database objects.** Gaps 1 and 2 — views, functions, stored
   procedures. Small, cheap to write, and explicitly listed in a requirements bullet.

Gap 8 (JavaScript) sits outside those three: it is required by Posting B alone, but at the same
level as C#.

---

## Postings E–I — industrial, intralogistics and automation, audited 2026-09-08

The standing summary above covers postings A–D only. A fifth audit, of five machine-market
postings around Modena and Reggio (System Logistics/Krones, E80 Group, Pulsar Industry, Infomotion,
and one vision role), produced a gap large enough to need its own document rather than a section
here:

**→ [`INDUSTRIAL-TRACK-PLAN.md`](INDUSTRIAL-TRACK-PLAN.md)**

The one-line version: `site/chapters/32-industrial-and-mes.html` covers the vocabulary well, the
`course/` tree covers it not at all, and module 17 §3 row 8 says so in writing. It is the only
topic in the repository a reader can read about but cannot run. The plan closes that with five
`Labs.Playground` demos, a machine layer for LogiFlow, a new module 28, and site chapters 32b/32c.
