# LogiFlow — Advanced .NET, taught as a real system

A production-shaped logistics and order-fulfilment platform, built in .NET 10 with Clean
Architecture, CQRS, EF Core and SQL Server — and written to be **read**, not just run.

This is not a tutorial app with a `TodoController`. It is the kind of codebase you would be
handed on your first day at a .NET shop, with every non-obvious decision explained in the file
where it was made, including the ones that are trade-offs rather than rules.

---

## Start here

You need the .NET 10 SDK and SQL Server. Nothing else — no Docker, no Redis, no collector.

```bash
# 1. Build, and run the application's own test suites
dotnet build
dotnet test LogiFlow.slnf      # 267 tests, all green

# 2. Run the API (it creates, migrates and seeds its database on first start)
cd src/LogiFlow.Api
dotnet run

# 3. In a second terminal, run the Blazor UI
cd src/LogiFlow.Web
dotnet run
```

Then open:

| What | Where |
|---|---|
| **The UI** | <http://localhost:5280> |
| **API reference (Scalar)** | <http://localhost:5199/scalar/v1> |
| **Health (readiness)** | <http://localhost:5199/health/ready> |
| **Telemetry — logs, traces, metrics** | <http://localhost:18888> (needs the `docker` profile) |

The default connection string is `Server=localhost` with **Windows authentication**, so there is no
password anywhere in the repository. Using a named instance or LocalDB? One line in
`src/LogiFlow.Api/appsettings.Development.json`, remembering that JSON needs the backslash doubled:

```
Server=localhost\\SQLEXPRESS;Database=LogiFlow;Integrated Security=True;TrustServerCertificate=True
Server=(localdb)\\MSSQLLocalDB;Database=LogiFlow;Integrated Security=True;TrustServerCertificate=True
```

To call a protected endpoint you need a token:

```bash
curl -X POST http://localhost:5199/api/dev/token \
  -H "Content-Type: application/json" \
  -d '{"email":"you@example.com","roles":["Admin"]}'
```

`requests/logiflow.http` has the whole flow ready to click through in VS Code or Rider.

**A fresh database has no orders.** The seeder creates products, customers and warehouses — the
reference data — but orders are something the domain creates, so the UI opens on an empty grid
until you make one. `requests/logiflow.http` walks through creating, filling and submitting an
order in about six clicks.

---

## What running without Docker costs you

Two of the three services the containers used to provide are genuinely optional, because the
application depends on **interfaces**, not on the products behind them. That is easy to claim and
hard to prove, so the repository proves it by shipping with both switched off.

- **A real distributed cache.** `ConnectionStrings:Redis` is empty, so
  `Infrastructure/DependencyInjection.cs` registers `AddDistributedMemoryCache`. Every line of
  cache-aside code runs unchanged. But read the name carefully: it implements `IDistributedCache`
  and is **not distributed** — it is per-process. Fine for one instance, silently wrong the moment
  you scale out, and a genuinely useful distinction to have felt rather than read.
- **The telemetry dashboard.** `Otlp:Endpoint` is empty, so the OTLP exporters are skipped. Traces
  and metrics are still *recorded* in-process and Serilog still writes to the console; nothing is
  shipped anywhere. Note that `Program.cs` decides this by asking *"is a collector configured?"*
  rather than *"is this Development?"* — a capability check, not an environment check.

- **A real SMTP server.** `Mailing:Transport` is `File`, so every message the application sends is
  written to `src/LogiFlow.Api/App_Data/mail` as an `.eml` file — the same bytes SMTP would carry,
  openable in Outlook or Thunderbird. What you lose is the delivery path itself; what you gain is
  that nothing can reach a real person. The container path swaps in Mailpit and a browser inbox.

Nothing is lost on the database side: the integration tests run against your local SQL Server, in
a throwaway `LogiFlow_Test_{guid}` database that is dropped afterwards. Every suite runs with no
container runtime installed.

## Prefer the containers? They still work

`docker-compose.yml` is still here, and the `docker` launch profile still points the application at
it:

```bash
docker compose up -d
docker compose ps                                  # wait for healthy, not just running

cd src/LogiFlow.Api
dotnet run --launch-profile docker

# second terminal, unchanged
cd src/LogiFlow.Web && dotnet run
```

That profile sets six environment variables, which **override** `appsettings.Development.json`
(configuration precedence — see module 13 section 2). Nothing in the repo is edited:

| Variable | Effect |
|---|---|
| `ConnectionStrings__SqlServer` | The SQL Server container on `localhost,1433`, SQL authentication |
| `ConnectionStrings__Redis` | `localhost:6380` — a real distributed cache |
| `Otlp__Endpoint` | `http://localhost:18889` — the Aspire Dashboard on <http://localhost:18888> |
| `Mailing__Transport`, `Mailing__Smtp__*` | A real SMTP send into Mailpit, with an inbox at <http://localhost:8025> |

This is the path to take when you want to *see* the difference: two API instances sharing a Redis,
or a trace spanning the UI and the API in one waterfall. To point the integration tests at a
container too, start one and set `LOGIFLOW_TEST_SQL` to its connection string.

---

## What is in here

```
src/
  LogiFlow.Domain           Business rules. Zero dependencies — not one NuGet package.
  LogiFlow.Application      Use cases: CQRS handlers, validation, the behaviour pipeline.
  LogiFlow.Infrastructure   EF Core, SQL Server, Redis, JWT, the outbox processor, the mailer.
  LogiFlow.Api              Minimal API endpoints, auth, error handling, observability.
  LogiFlow.Web              Blazor Web App (InteractiveServer). A CLIENT of the API, over HTTP.
  LogiFlow.Wcs              The warehouse control system: the process that talks to the machines.
                            A WORKER, not a web app — a WCS that dies when an app pool recycles
                            is a stopped line. Runs a SIMULATED floor with no hardware at all,
                            and survives a restart: pending transport orders resume, in-flight
                            ones are cancelled, and zone occupancy is rebuilt from the floor.
  LogiFlow.Academy.Api      Accounts for the tutorial site, and its static host. Standalone:
                            references none of the layers above. SQLite by default, so it runs
                            with no database server and no configuration at all.

tests/
  LogiFlow.Domain.Tests         56 tests. No mocks, no database, milliseconds.
  LogiFlow.Application.Tests    Handlers, with NSubstitute for I/O only.
  LogiFlow.Api.IntegrationTests Real HTTP against a real SQL Server, in a throwaway database.
                                Also the versioning, SignalR and gRPC surfaces, end to end.
  LogiFlow.ArchitectureTests    Tests over the dependency graph. These keep the design honest.
  LogiFlow.Infrastructure.Tests 48 tests. Retry policy, redirect guard, options validation, MIME,
                                and the commissioning run: an hour of simulated warehouse.
  LogiFlow.Academy.Api.Tests    83 tests over the accounts service, on SQLite :memory:.

tools/
  doctor.cs         Every cross-reference in the repository, checked in one command.
                    Fourteen checks; the CI gate. `dotnet run tools/doctor.cs`
  viva-deck.cs      Regenerates the site's viva deck from course/GOLDEN-RULES.md.
                    A .NET 10 file-based app: no .csproj, not in the solution,
                    `dotnet run tools/viva-deck.cs`.

labs/
  Labs.Exercises    Graded exercises. 113 tests, currently RED. Make them green.
  Labs.Playground   34 runnable demos — watch deferred execution, a lost update, a
                    memory leak and the async deadlock actually happen.
  Labs.Benchmarks   BenchmarkDotNet. Every performance claim in the course is checkable.
  Labs.LoadTests    NBomber. The other half: what the SYSTEM does under concurrent load,
                    which is where connection pools and row contention live. Needs the
                    API running, so it is not in LogiFlow.slnf.
  opc-ua.cs         A real OPC UA server and client, talking to each other offline with
                    no hardware. A .NET 10 file-based app rather than a project, to keep
                    the OPC Foundation stack out of the solution:
                    `dotnet run labs/opc-ua.cs`.

course/             The guided path through all of it. Start at course/README.md.
                    29 modules, and 57 deeper chapters sitting beside them — one per topic,
                    linked from a contents table at the top of each module. The `Covered in:`
                    comments throughout src/ and tests/ point straight at them, so reading a
                    class and reading its chapter are one gesture.
                    course/GOLDEN-RULES.md   every module's card, in module order.
                    course/LAWS-OF-CSHARP.md the same knowledge by concept, in twelve books.
                    course/SOLUTIONS.md      worked answers to the labs, with the reasoning.

site/               A browsable W3Schools-style tutorial: 53 ordered chapters with a
                    sidebar, built around one real .NET job advert — thirty of them the advert
                    never mentions but the Modena/Bologna/Milano market keeps asking for,
                    including WinForms/WPF, industrial/MES, Business Central and the screening
                    test. Every idea explained twice — once simply, once the way you would
                    answer it in an interview. Open site/index.html. No build step.

                    It also grades you: 498 questions, a test at the end of every chapter, a
                    spaced-repetition deck built from whatever you got wrong, a timed mock
                    exam, sticky notes, an Italian/English glossary, and one course-mastery
                    percentage that is three-quarters test score — so it cannot be moved by
                    scrolling. Sign in (see below) to carry all of it between machines.

                    And one surface with no options to pick from: site/viva.html draws a
                    Golden rule, makes you produce the justification in your own words, and
                    only then shows you the written one. Recognition is what multiple choice
                    measures; an interview measures whether you can say it.

                    site/simulate.html adds the conditions the viva cannot: a clock, a mixed
                    round, no pausing and no going back — opening with "mi parli di lei" and
                    closing with "ha domande per noi?". It gives you a transcript, never a
                    score, because a self-marked percentage is a number about your generosity.

                    site/italiano.html is the sentences rather than the words. The glossary
                    covers vocabulary; this covers what you actually say when someone asks
                    "spiegami perché" and you have ninety seconds — including the one line
                    worth memorising, which is how to say you have not done something.

                    site/cv.html builds an Italian CV and lettera, then LINTS them against
                    chapter 36: the GDPR line, CEFR levels, bullets that describe a result
                    rather than presence, at least one number, one page.

                    The whole site works offline and installs as an app — all 53 chapters
                    are precached on first visit. It never caches the accounts API, because
                    a cached "here is your profile" would be a lie with your progress on it.

docs/               Why this repository is the way it is: seven architecture decision
                    records, including the two decisions this codebase got wrong first and
                    the load test that found one of them. Not course material — course/ is
                    for how things work, docs/ is for why they are like this.

deploy/             Three Dockerfiles and a Kubernetes manifest. The compose file grew an
                    `app` profile that runs LogiFlow itself: two API instances sharing one
                    Redis, so "the in-memory cache is silently wrong the moment you scale
                    out" is something you can watch rather than believe.

.github/            CI. Six jobs that fail for six different reasons, one of which is
                    "somebody committed the answers over the lab exercises".
```

### Running the tutorial with accounts

```bash
dotnet run --project src/LogiFlow.Academy.Api
# then open http://localhost:5280
```

One process serves the site *and* the API. No database to install: it creates a SQLite file
on first run. Several people can use one deployment, and the same person can move between a
laptop and a phone — a two-device edit is merged rather than one side overwriting the other.

The service is also worth reading on its own: PBKDF2 password hashing, JWT with rotating
refresh tokens, optimistic concurrency and a hand-written CORS/rate-limit/middleware setup,
in six commented files. See
[`src/LogiFlow.Academy.Api/README.md`](src/LogiFlow.Academy.Api/README.md).

---

## How to actually learn from this

**Do not read it front to back.** Work the modules in `course/`, and for each one:

1. **Read the lesson** — it explains the concept and names the files that use it.
2. **Read those files** — the comments explain *why*, not what. That is the real material.
3. **Do the lab** — `dotnet test labs/Labs.Exercises` until green.
4. **Break something** — the fastest way to understand a guard is to remove it and watch which
   test fails. `git checkout .` puts it back.
5. **Run the demo** — most modules name one. `cd labs/Labs.Playground && dotnet run list`.
   Reading that a race condition exists is a sentence; watching 600,000 increments disappear is
   the concept.
6. **Read the module's golden rules** — the card at the end. If you cannot say why a rule is true,
   that is the paragraph to go back to. All twenty-nine cards are collected in
   [`course/GOLDEN-RULES.md`](course/GOLDEN-RULES.md), and the same material organised by concept
   is in [`course/LAWS-OF-CSHARP.md`](course/LAWS-OF-CSHARP.md).
7. **Say them back** — `site/viva.html` turns those same 378 rules into a drill that hides the
   answer until you have written or spoken yours, then schedules the ones you fumbled. Reading a
   card you agree with is the easiest thing in this repository to mistake for knowing it.

The comments in the source are not decoration; they are roughly half the course. A representative
sample of what they cover:

- Why `Money` is a `readonly record struct` and why `decimal` instead of `double`
  → `src/LogiFlow.Domain/ValueObjects/Money.cs`
- How MediatR actually resolves a handler, implemented from scratch in ~200 lines
  → `src/LogiFlow.Application/Abstractions/Messaging/Dispatcher.cs`
- Why `Expression<Func<T,bool>>` is not `Func<T,bool>`, and why EF Core cares enormously
  → `src/LogiFlow.Domain/Common/Specifications/ExpressionExtensions.cs`
- The dual-write problem and the transactional outbox that solves it
  → `src/LogiFlow.Infrastructure/Persistence/Outbox/OutboxMessage.cs`
- Why a queue table claims its rows with `UPDLOCK, READPAST` and counts the attempt *before*
  sending → `src/LogiFlow.Infrastructure/Mailing/QueuedEmailStore.cs`
- Why the pipeline is ordered Logging → Validation → Caching → Transaction, and what breaks
  at each other ordering → `src/LogiFlow.Application/DependencyInjection.cs`

---

## Things this codebase does that tutorials usually skip

| | Where |
|---|---|
| Strongly-typed IDs, so `Ship(warehouseId, orderId)` is a compile error | `Domain/Common/Ids.cs` |
| `Result<T>` instead of exceptions for expected failures (measured: **4,500× faster**) | `Domain/Results/Result.cs` |
| Optimistic concurrency with `rowversion`, and a test that fires 20 parallel reservations | `Domain/Common/AggregateRoot.cs` |
| Domain events dispatched *inside* the transaction | `Infrastructure/Persistence/Interceptors/` |
| A transactional outbox for anything crossing a process boundary | `Infrastructure/Persistence/Outbox/` |
| Email queued *inside* the transaction, claimed atomically, retried with jittered backoff, and redirected away from real customers outside production | `Infrastructure/Mailing/` |
| RFC 9457 Problem Details on every error, with stable machine-readable codes | `Api/Infrastructure/ResultExtensions.cs` |
| Filtered indexes, CHECK constraints, and a sequence for gapless-ish order numbers | `Infrastructure/Persistence/Configurations/` |
| Architecture tests that fail the build if someone references EF Core from the Domain | `tests/LogiFlow.ArchitectureTests/` |
| A real CVE caught by the build, and the judgement call in fixing it | `Directory.Packages.props` |

---

## Requirements

- **.NET 10 SDK** (`dotnet --version` should print 10.x)
- **SQL Server** — Developer or Express edition, both free. LocalDB works too
- **Docker Desktop** — *optional*, only for the container path and the telemetry dashboard

EF Core tooling, if you want to add migrations yourself:

```bash
dotnet tool install --global dotnet-ef
```

---

## Common commands

```bash
dotnet build                                   # whole solution, warnings are errors in src/

dotnet test LogiFlow.slnf                      # every suite except the labs — 267 tests
dotnet test tests/LogiFlow.Domain.Tests        # fast feedback loop while working on rules
dotnet test tests/LogiFlow.Academy.Api.Tests   # the accounts service — 83 tests, SQLite in-memory
dotnet test labs/Labs.Exercises                # YOUR HOMEWORK — 113 tests, deliberately red

# `dotnet test` with no argument runs the labs too, so it reports failures until you have
# done the exercises. That is intentional; LogiFlow.slnf excludes them for CI-style runs.

# The tutorial site
dotnet run --project src/LogiFlow.Academy.Api  # site + accounts on http://localhost:5280
python -m http.server 8777 --directory site    # or just the site, no accounts

dotnet run tools/doctor.cs                     # every cross-reference in the repo, checked
dotnet run tools/doctor.cs --update            # after adding a quiz question

dotnet run tools/viva-deck.cs                  # rebuild the viva deck after editing
dotnet run tools/viva-deck.cs --check          # course/GOLDEN-RULES.md

# Mutation testing: does the suite actually TEST anything, or just run?
dotnet tool restore
dotnet stryker                                 # slow. CI runs it weekly.

# Load testing. Needs the API running in another terminal.
dotnet run --project labs/Labs.LoadTests

# The whole application in containers - two API instances behind one Redis
docker compose --profile app up -d --build

cd labs/Labs.Playground && dotnet run          # list the 34 demos
cd labs/Labs.Benchmarks && dotnet run -c Release --filter '*Linq*'

# Migrations
dotnet ef migrations add <Name> --project src/LogiFlow.Infrastructure --output-dir Persistence/Migrations
dotnet ef database update --project src/LogiFlow.Infrastructure

# Only if you are using the optional container path
docker compose down       # stop, keep data
docker compose down -v    # stop and wipe the database
```

---

## Troubleshooting

**`A network-related or instance-specific error occurred`**
SQL Server is not running, or the instance name is wrong. Confirm with
`sqlcmd -S localhost -E -C -Q "SELECT @@VERSION"`. On a named instance the SQL Server Browser
service also has to be running. On the container path, it takes ~30 seconds on first boot — check
`docker compose ps` and wait for `(healthy)`, not just `Up`.

**`Login failed for user ...`**
SQL Server is reachable but your Windows account has no login on it. Add one, or connect as an
administrator once and create it. Normal when somebody else installed the instance.

**`Cannot open database "LogiFlow"`**
The process could not create it. Migrations run on startup only in Development, so check
`ASPNETCORE_ENVIRONMENT` and that your login may create databases.

**Stray `LogiFlow_Test_*` databases**
A crashed integration-test run leaves its throwaway database behind. They are safe to drop:
`SELECT name FROM sys.databases WHERE name LIKE 'LogiFlow[_]Test[_]%'`.

**`Bind for 0.0.0.0:6379 failed: port is already allocated`** *(container path only)*
You already have Redis running. Ours is deliberately mapped to **6380** to avoid this; if
something else owns that too, change the mapping in `docker-compose.yml` and the `Redis`
connection string in the `docker` launch profile. If you have SQL Server installed natively it
already owns 1433, and the container will collide with it.

**No email arrives**
By design. `Mailing:Transport` defaults to `File` in Development, so messages are written to
`src/LogiFlow.Api/App_Data/mail/*.eml` — open one in any mail client. Nothing is queued at all
until the transaction that produced it commits, and delivery then happens on the worker's next
tick (2 seconds locally). If the folder stays empty, look in `logiflow.QueuedEmails`: a row with a
climbing `AttemptCount` and a `LastError` is a delivery failure, and one with `AbandonedAtUtc` set
has been given up on. Set `Mailing:Transport` to `Smtp` with the `docker` profile to send into
Mailpit and read it at <http://localhost:8025>.

**`Mailing:Smtp:Host is required when Mailing:Transport is Smtp`**
Startup validation, doing its job — the same deliberate failure as the signing key below. The
mailing options are validated with `ValidateOnStart`, so a misconfiguration stops the process
rather than silently losing every customer email.

**`Jwt:SigningKey is not configured`**
You are running outside Development without setting it. That failure is deliberate — see
`AuthenticationSetup.AddLogiFlowAuth`, which validates configuration at startup rather than
letting you discover it at the first login.

**`Academy:Jwt:SigningKey must be set to at least 32 bytes`**
The same rule in the Academy service. In Development it generates one into
`src/LogiFlow.Academy.Api/App_Data/signing.key` on first run; anywhere else you supply it,
because a committed signing key lets anyone mint a token for any user.

**The tutorial's home page 404s but every chapter works**
That was a real bug, fixed, and worth knowing about because it is not obvious: static-file
middleware stands aside when an endpoint has already been matched, and `WebApplication`
inserts `UseRouting` at the top of the pipeline unless you call it yourself. If you add a
catch-all route to `LogiFlow.Academy.Api`, keep `app.UseRouting()` **below** the static
files. `AuthFlowTests.Every_public_page_and_asset_is_served_anonymously` covers it.

---

## A note on honesty

Some things here are deliberately imperfect and labelled as such — the confirmation email sent
inside a transaction, the in-memory warehouse filter that would not scale, the read-model
duplication that CQRS costs you. They are left in, with the reasoning and the fix written next
to them, because recognising a trade-off is a more useful skill than memorising a rule.

Where a shortcut was taken for teaching reasons, the comment says so.

**And where the repository is simply not as good as it looks, it says that too.**
`dotnet test` reports 234 passing tests. `dotnet stryker` reports that those tests kill
**33.74%** of the mutants in the domain — that `Shipment.cs` scores 0.00%, and that `Money.cs`,
the type this codebase makes the most noise about, scores 9.68% with 52 of its 78 mutants
surviving. Both numbers are true, and the second one is the honest gloss on the first.

That is left visible rather than fixed quietly, because a test count is the easiest metric in
software to mistake for a guarantee. See [`tests/README.md`](tests/README.md) and
[ADR 8](docs/adr/0008-mutation-testing-as-a-ratchet.md).
