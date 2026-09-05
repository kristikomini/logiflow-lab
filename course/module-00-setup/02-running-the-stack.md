# 1, 4–5. Running the stack

> Part of [Module 00 — Setup and tooling](README.md), sections 1, 4 and 5.
> Next: [2–3. The build system is code too](03-build-system.md)

---

Everything this course does is checkable against a running system, so this is the chapter that makes
the rest possible. One command, then the handful of details that cost people an evening.

```bash
dotnet run --project src/LogiFlow.Api
```

That is the whole procedure, provided you have SQL Server installed. In Development the application
applies its own migrations and seeds sample data on startup, so the database does not exist before
you run it and does exist afterwards. There is nothing to create by hand.

## What you need installed

| Thing | Needed? | Notes |
|---|---|---|
| .NET 10 SDK | yes | `global.json` pins `10.0.300`; `dotnet --version` must agree |
| SQL Server | yes | Developer or Express edition, both free. LocalDB works too |
| Redis | **no** | the cache falls back to in-memory — see below |
| A telemetry collector | **no** | traces and metrics are still produced, just not exported |

Two of those four are deliberately optional, and that is a design decision rather than a
convenience. The application talks to `IDistributedCache`, not to Redis; it talks to OpenTelemetry,
not to a dashboard. An abstraction you can actually run without is the only kind that has been
proved to be an abstraction.

### The connection string

`src/LogiFlow.Api/appsettings.Development.json`:

```json
"SqlServer": "Server=localhost;Database=LogiFlow;Integrated Security=True;TrustServerCertificate=True;Encrypt=True"
```

Three parts of that are worth understanding rather than copying.

**`Integrated Security=True`** is Windows Authentication: no username, no password, because your
Windows account *is* the credential. That is not merely tidy — it is why there is no secret in this
file to leak. It is also how most on-premise Italian shops connect to SQL Server, so it is worth
being fluent in it rather than reaching for `sa` out of habit.

**`Server=localhost`** is the default instance. If you installed a named instance — Express does
this by default — you need its name, and in JSON the backslash must be escaped:

```json
"Server=localhost\\SQLEXPRESS"      // named instance
"Server=(localdb)\\MSSQLLocalDB"    // LocalDB
```

The doubled backslash is JSON's rule, not SQL Server's. A single one is an invalid escape sequence,
and the error you get back talks about JSON rather than about databases, which is why this wastes
people's time.

**`TrustServerCertificate=True`** is required because a default installation encrypts with a
self-signed certificate. Note what this does *not* mean: `Encrypt=True` is still on, so the
connection is encrypted — you have simply told the client not to verify who is on the other end.
On your own machine, connecting to your own SQL Server, that is a reasonable trade. In production
it is not: there you install a real certificate and drop this setting. `Microsoft.Data.SqlClient`
has defaulted `Encrypt` to `True` since version 4.0, which is why this appears in every modern
connection string and appeared in almost no older one.

### Which instance am I even talking to?

```bash
sqlcmd -S localhost -E -C -Q "SELECT @@VERSION"
```

`-E` is integrated security, `-C` trusts the certificate. If that prints a version banner, the
connection string above will work. If it does not, nothing else in this course will, so fix it here
rather than in the application.

## Running without Redis

`ConnectionStrings:Redis` is empty by default, and `LogiFlow.Infrastructure` reads it as a
capability check:

```csharp
if (string.IsNullOrWhiteSpace(redis))
{
    services.AddDistributedMemoryCache();   // NOT distributed, despite the interface
}
else
{
    services.AddStackExchangeRedisCache(/* ... */);
}
```

Every line of cache-aside code in [module 10](../module-10-cross-cutting/03-caching.md) runs
unchanged either way. That is the entire argument for coding against `IDistributedCache` instead of
against `StackExchange.Redis`, demonstrated rather than asserted.

Be precise about what you lose, though, because the name lies. `AddDistributedMemoryCache`
implements `IDistributedCache` and is **not distributed**: each process gets its own copy. One
instance is fine. Two instances will disagree with each other, and they will do it silently — a
stale entry looks exactly like a fresh one. To see that failure and its fix, start Redis (the
[container path](#the-stack) below) and fill the key in.

## Running without a collector

`Otlp:Endpoint` is empty by default. `Program.cs` treats it as a capability check too:

```csharp
bool otlpEnabled = !string.IsNullOrWhiteSpace(builder.Configuration["Otlp:Endpoint"]);
```

With no endpoint, the instrumentation is still registered — `Activity` and `Meter` objects are
created, spans are still opened and closed — and only the *exporter* is skipped. Serilog keeps
writing to the console throughout.

Note the shape of that check, because it is the transferable part. It asks **"is a collector
configured?"**, not **"is this Development?"**. An environment check would have been shorter and
would break the moment somebody runs a collector locally or points Development at a shared one.
Configure on capability, not on environment name.

## Turn on the SQL log

The single most useful switch in the repository. In
`src/LogiFlow.Api/appsettings.Development.json`:

```json
"Microsoft.EntityFrameworkCore.Database.Command": "Information"
```

Every query now prints with its parameters.

**Leave it on for modules 06, 07 and 09.** Reading the generated SQL is the fastest way to build an
accurate model of what your LINQ actually costs, and several later chapters are written on the
assumption that you can see it — spotting an [N+1](../module-06-efcore/04-n-plus-one.md) means
recognising the same query shape repeated with a different parameter, and you cannot recognise what
you cannot see.

Turn it off before you measure anything. Console logging is slow enough to distort a benchmark, and
[module 14](../module-14-performance/) will tell you the same thing about timings generally.

## When it does not work

**"A network-related or instance-specific error occurred."** SQL Server is not running, or the
instance name is wrong. Check the service is started, then check the name with the `sqlcmd` line
above. On a named instance, the SQL Server Browser service also has to be running.

**"Login failed for user ..."** SQL Server is reachable but your Windows account has no login on it.
Either add one, or connect as an administrator once and create it. This is the normal outcome when
the instance was installed by somebody else.

**"The certificate chain was issued by an authority that is not trusted."** `TrustServerCertificate`
is missing from the connection string. See above for why it is there and why it is not a production
setting.

**"Cannot open database 'LogiFlow'."** Usually means the process could not create it — migrations
run on startup only in Development, so confirm `ASPNETCORE_ENVIRONMENT=Development` and that your
login is allowed to create databases.

## <a id="the-stack"></a>The container path (optional)

Everything above needs SQL Server on your machine. If you would rather not install it — or you are
on a machine where you cannot — `docker-compose.yml` is still in the repository and still works.
It is no longer the default, but nothing about it has been removed.

```bash
docker compose up -d
docker compose ps                                  # wait for healthy, not just running
dotnet run --project src/LogiFlow.Api --launch-profile docker
```

| Service | Host port | Why it is there |
|---|---|---|
| SQL Server 2022 | 1433 | the database, Developer edition |
| Redis | **6380** | distributed cache — [module 10 section 4](../module-10-cross-cutting/03-caching.md) |
| Aspire Dashboard | 18888 / 18889 | logs, traces and metrics over OTLP — [module 11](../module-11-observability/) |

The `docker` launch profile is what connects the two halves. It sets three environment variables:

```json
"ConnectionStrings__SqlServer": "Server=localhost,1433;Database=LogiFlow;User Id=sa;Password=...",
"ConnectionStrings__Redis": "localhost:6380",
"Otlp__Endpoint": "http://localhost:18889"
```

The double underscore is how .NET maps an environment variable onto a nested configuration key:
`ConnectionStrings__SqlServer` overrides `ConnectionStrings:SqlServer`. Environment variables sit
*above* `appsettings.Development.json` in the precedence order, which is exactly why a profile can
redirect the whole application without editing a file. That ordering is the subject of
[module 13 section 2](../module-13-deployment/02-configuration.md).

This path also uses `sa` and a password, where the native path uses Windows Authentication. That is
not sloppiness — a Linux container has no domain account to integrate with, so SQL authentication is
the only option available inside it. Worth knowing before an interviewer asks why the two strings
differ.

### Redis is on 6380, not 6379

The single most likely thing to bite you, and it is deliberate. A locally installed Redis — or
another project's container — very often already owns 6379, and the resulting
*"port is already allocated"* blocks the **whole** stack from starting, not just Redis.

Inside the compose network it is still 6379. Only the host mapping shifts. So the connection string
above says `localhost:6380` and any container-to-container connection string would say `redis:6379`.

That distinction — **host port versus network port** — is the one people get wrong when they later
containerise the API itself. Inside a container, `localhost` means *that container*, not your
machine.

### The healthcheck is not decoration

```yaml
healthcheck:
  test: ["CMD-SHELL", "/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P \"$$MSSQL_SA_PASSWORD\" -C -Q 'SELECT 1' || exit 1"]
  interval: 10s
  start_period: 30s
```

**SQL Server accepts a TCP connection several seconds before it will accept a login.** A readiness
check that only waits for the port to open will hand you a "ready" container that immediately
refuses to authenticate.

So the check runs an actual query. `-C` trusts the self-signed certificate; `$$` escapes the dollar
so Compose passes it through to the shell rather than substituting it itself.

A container that is *running* is not a database that is *ready*. That gap is the cause of a whole
genre of flaky test — and it is one of the reasons the integration tests in
[module 12 section 4](../module-12-testing/03-integration-testing.md) no longer start a container
at all.

### `down` versus `down -v`

```bash
docker compose down       # stop the containers, KEEP the data
docker compose down -v    # stop AND delete the volumes — the database is gone
```

`-v` deletes the named volumes. It is the correct way to get back to a clean seeded database, and
the wrong way to end a session you wanted to continue tomorrow. Read the flag before you type it.

### Container-path failures

**"Port is already allocated."** Something owns 1433 or 6380. `docker ps` to find it, or change the
host mapping in `docker-compose.yml` — and remember to change the connection string to match. Note
that if you also have SQL Server installed natively it already owns 1433, and the two paths will
fight over it.

**SQL Server exits immediately.** Almost always memory: it needs about 2 GB and Docker Desktop
often defaults lower. `docker logs logiflow-sql` says so plainly.

**"Login failed for user 'sa'."** The container is up and not yet ready — see the healthcheck above.
Wait for `docker compose ps` to report `healthy` rather than merely `running`.

**A password error on first start.** `MSSQL_SA_PASSWORD` must meet SQL Server's complexity rules;
the one in the compose file does. If you change it, change it in the launch profile too, and delete
the volume — the password is set on first initialisation only.

## Try it

```bash
dotnet run --project src/LogiFlow.Api
```

Then open `/scalar/v1`, call an endpoint, and watch the SQL appear in the console with its
parameters. That loop — call it, read the SQL — is the one this whole course runs on.

Then prove the capability checks are real. Stop the application, put `localhost:6380` into
`ConnectionStrings:Redis` without starting Redis, and run it again. The failure is a connection
error, not a silent fallback: configuration that claims a capability you do not have is worse than
configuration that claims nothing.

## What to remember

- `dotnet run --project src/LogiFlow.Api`. Migrations and seed data happen on startup in Development.
- SQL Server is the one hard prerequisite. Redis and a collector are both genuinely optional.
- `Integrated Security=True` means there is no password in the file to leak.
- In JSON, a named instance needs a **doubled** backslash: `localhost\\SQLEXPRESS`.
- `TrustServerCertificate=True` still encrypts — it just stops verifying. Not for production.
- Check capability ("is an endpoint configured?"), not environment ("is this Development?").
- The compose stack still works via the `docker` launch profile; it is an alternative, not the default.
- Host port and network port are different. Inside a container, `localhost` is that container.
- Turn the SQL log on for modules 06, 07 and 09, and off before you measure anything.

**Code:** [`appsettings.Development.json`](../../src/LogiFlow.Api/appsettings.Development.json) ·
[`launchSettings.json`](../../src/LogiFlow.Api/Properties/launchSettings.json) ·
[`docker-compose.yml`](../../docker-compose.yml)

**Next:** [2–3. The build system is code too](03-build-system.md)
