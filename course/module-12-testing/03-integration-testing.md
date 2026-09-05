# 4. Integration tests — and why not the in-memory provider

> Part of [Module 12 — Testing](README.md), section 4.
> Previous: [3. Application tests](02-unit-testing-handlers.md) ·
> Next: [5. Architecture tests](05-architecture-tests.md)

---

An integration test boots **the real application** and drives it over **real HTTP** against a **real
database**. Not a mock in sight — that is the point. Unit tests prove each part works in isolation;
an integration test proves the parts were wired together correctly, which is a different and
frequently untested claim.

## The factory

```csharp
public sealed class LogiFlowApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // A fresh database per run. The GUID is the isolation.
    private readonly string _database = $"LogiFlow_Test_{Guid.NewGuid():N}";

    private const string DefaultServer =
        "Server=localhost;Integrated Security=True;TrustServerCertificate=True;Encrypt=True";

    private string ConnectionString => new SqlConnectionStringBuilder(
        Environment.GetEnvironmentVariable("LOGIFLOW_TEST_SQL") is { Length: > 0 } configured
            ? configured
            : DefaultServer)
    {
        InitialCatalog = _database,
    }.ConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("ConnectionStrings:SqlServer", ConnectionString);

    async Task IAsyncLifetime.InitializeAsync()
    {
        using IServiceScope scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LogiFlowDbContext>();
        await context.Database.MigrateAsync();      // CREATES the database, then builds the schema
    }
}
```

**`WebApplicationFactory<Program>` runs the real `Program.cs`** — the same DI registrations, the same
middleware order, the same endpoint mapping, the same
[pipeline behaviours](../module-08-cqrs/05-pipeline-behaviors.md). Nothing is stubbed.

**Only the connection string is replaced.** That is the discipline: the more you swap, the less the
test proves. If you find yourself substituting a repository here, you have written a slow unit test.

**`MigrateAsync` creates the database.** It connects to `master` when the target does not exist, so
there is no `CREATE DATABASE` anywhere in the test code. The database is real SQL Server with the
real schema built by the real migrations — which means a broken migration fails your test suite,
not your next deployment.

**`UseSetting`, not just the in-memory configuration source.** Under minimal hosting the application
reads `ConnectionStrings:SqlServer` *while `Program.cs` is still executing*, before a
`ConfigureAppConfiguration` callback has been layered on. Get this wrong and the override loses to
`appsettings.Development.json` — and the tests quietly run against your real `LogiFlow` database.
That is not a subtle bug. That is the difference between a test suite and a data-loss incident.

## Where the database comes from

The suite points at **a SQL Server you already have installed**, and gives every run its own
database named `LogiFlow_Test_{guid}`, dropped in teardown.

Set `LOGIFLOW_TEST_SQL` to send it somewhere else — a named instance, LocalDB, a build agent's
server, a container you started yourself. Whatever `Initial Catalog` that string carries is ignored;
the per-run name always wins, because the isolation is not negotiable.

### Why not Testcontainers?

This suite used [Testcontainers](https://dotnet.testcontainers.org/) until the repository moved off
Docker, and the honest version of the trade-off is worth knowing — you will be asked.

| | Testcontainers | Local server, per-run database |
|---|---|---|
| Clean state | guaranteed: new container every time | new database every time; the *server* is shared |
| Version pinning | exact, by image tag | whatever you installed |
| Prerequisite | a running Docker daemon | a SQL Server installation |
| Cost per run | ~10s container start | ~1s |
| CI | needs Docker on the agent | needs a SQL service on the agent |

What you gain is speed and one fewer thing to install. What you give up is **version pinning**, and
that is a real loss: with a container tagged `2022-CU26-ubuntu-22.04`, a test that fails today and
passed last month is a code change. Against whatever SQL Server each developer happens to have, it
might genuinely be their SQL Server build. Neither is wrong — but only one of them is honest about
which risk it accepted.

Teardown is the part people underestimate:

```csharp
await context.Database.EnsureDeletedAsync();
```

`EnsureDeletedAsync` is doing more than `DROP DATABASE`. The SQL Server provider clears the
connection pool and sets `SINGLE_USER WITH ROLLBACK IMMEDIATE` first. Without that, one pooled
connection still parked on the database is enough to fail the drop with *"database is currently in
use"* — the classic half-hour of confusion when somebody hand-rolls this teardown. A container has
no equivalent problem, because you throw the whole machine away. Owning the cleanup is the price of
not owning a container.

The drop is also wrapped in a `catch`: a failed cleanup must never turn a green run red. The cost is
one stray database on the server, which is exactly why the name carries a `LogiFlow_Test_` prefix —
they are trivial to find and drop in bulk.

```sql
SELECT name FROM sys.databases WHERE name LIKE 'LogiFlow[_]Test[_]%';
```

## Why not `UseInMemoryDatabase`

This is a standard interview question and the answer is a list, not an opinion. The EF in-memory
provider **is not a database** — it is a LINQ-to-Objects shim wearing a `DbContext` costume:

- **No schema.** It happily stores a 500-character string in a column mapped as `nvarchar(20)`.
- **No constraints.** Unique indexes, foreign keys and `CHECK` constraints silently do nothing — so
  the [duplicate-order-number guarantee](../module-06-efcore/03-fluent-configuration.md) is untested.
- **No real transactions.** `BeginTransaction` is a no-op, so rollback behaviour is unverified.
- **No `rowversion`.** [Optimistic concurrency](../module-07-sql-and-transactions/04-concurrency.md)
  cannot be tested at all — the one thing you most want an integration test for.
- **No raw SQL**, so any `FromSql` path is untestable.
- **Different LINQ translation.** Queries that throw against SQL Server pass here, because everything
  is evaluated in memory.

Every one of those is a place where **the test passes and production fails**, which is worse than
having no test — it is a false negative you trust. Microsoft's own documentation now recommends
against it for this purpose.

SQLite in-memory is a middle ground: real SQL, real transactions, real constraints, but a different
dialect and no `rowversion`. Better than the EF provider; still not the thing you deploy against.

## Writing one

```csharp
[Fact]
public async Task An_order_can_be_created_submitted_and_read_back()
{
    HttpClient client = await _factory.CreateAuthenticatedClientAsync("WarehouseStaff");

    HttpResponseMessage created = await client.PostAsJsonAsync("/api/orders", new
    {
        customerId = SeedData.CustomerId,
        lines = new[] { new { productId = SeedData.ProductId, quantity = 2 } }
    });

    created.StatusCode.ShouldBe(HttpStatusCode.Created);
    Guid id = await created.Content.ReadFromJsonAsync<Guid>();

    (await client.PostAsync($"/api/orders/{id}/submit", null))
        .StatusCode.ShouldBe(HttpStatusCode.NoContent);

    OrderDetailDto? order = await client.GetFromJsonAsync<OrderDetailDto>($"/api/orders/{id}");
    order!.Status.ShouldBe(nameof(OrderStatus.Submitted));
}
```

**Test through the API, not through the service container.** Resolving a handler from
`_factory.Services` and calling it skips routing, model binding, authentication, the exception
handler and serialisation — which is most of what an integration test exists to cover.

**One test, one business scenario, several requests.** The value is in the sequence: create, submit,
read back. A test that does one request is usually better written as a unit test.

**Authentication is part of the wiring.** A helper that mints a token for a given policy tests the
real `[Authorize]` path rather than disabling it.

## Isolation between tests

The problem nobody warns you about: tests in a collection share one database, so test A's data is
visible to test B, and they fail differently depending on order. The per-run database isolates one
*run* from another; it does nothing for one *test* against another.

Three strategies, in increasing cost:

| Strategy | How | Cost |
|---|---|---|
| **Unique data** | every test creates its own customer/product with fresh ids | free, and the default here |
| **Respawn** | delete all rows between tests with the `Respawn` package | fast, resets everything |
| **Transaction rollback** | wrap each test and never commit | fastest, and it cannot test transactional behaviour — which is often the point |

Unique data is the one to reach for first. It also keeps tests parallelisable, which the other two do
not.

## Keep them few

Integration tests are the top of [the pyramid](README.md#1-the-pyramid-as-built-here) for a reason:
they are slower than unit tests, they need a database, and they fail for environmental reasons that
have nothing to do with your code.

**Test the paths that only integration can prove:**

- the happy path of each major workflow, end to end
- authentication and authorisation actually applying
- a real concurrency conflict producing a 409
- serialisation of the shapes clients depend on

**Do not test every validation rule here.** Those belong in
[handler tests](02-unit-testing-handlers.md), which run in milliseconds. A validation matrix at the
integration level costs minutes of CI on every push and proves nothing extra.

In CI this means a SQL Server the agent can reach — a service container, a hosted instance, or
LocalDB on a Windows agent — pointed at with `LOGIFLOW_TEST_SQL`. State it in the pipeline's
prerequisites; see [module 13](../module-13-deployment/04-migrations-in-ci.md).

## The mistakes

**`UseInMemoryDatabase`.** Covered above; it is the big one.

**An override that loses to `appsettings`.** The `UseSetting` point above. Tests that silently run
against your development database look like they are passing.

**Mocking inside an integration test.** Every substitution reduces what it proves.

**Bypassing HTTP.** Skips most of the layer under test.

**Order-dependent tests.** They pass locally and fail in CI, or vice versa, and the diagnosis is
miserable.

**A test per validation rule.** Slow, and duplicating cheap unit tests.

**Leaking databases.** A teardown that only runs on the happy path leaves one behind per crashed run.
Name them so you can find them.

## Try it

```bash
dotnet test tests/LogiFlow.Api.IntegrationTests
```

Watch the database get created, the migrations apply, and the tests run against real SQL Server.
Then break something only a real database would catch — set a string longer than its mapped
`nvarchar` length, or trigger two concurrent updates — and confirm the test goes red. Then imagine
that same test against the in-memory provider, where both would pass.

Then, while the suite runs, watch the database appear and vanish:

```sql
SELECT name FROM sys.databases WHERE name LIKE 'LogiFlow[_]Test[_]%';
```

## What to remember

- An integration test runs the real `Program.cs` over real HTTP against a real database.
- Replace only the connection string. Every extra substitution weakens the claim.
- Use `UseSetting` for that override — configuration is read before `ConfigureAppConfiguration` runs.
- The EF in-memory provider has no schema, no constraints, no transactions and no `rowversion`.
- It produces tests that pass while production fails — a false negative you trust.
- A per-run database gives run-level isolation without Docker; you trade away version pinning.
- `EnsureDeletedAsync` clears the pool and forces `SINGLE_USER` — hand-rolled drops fail on pooling.
- Drive through the API, not through the service container.
- Isolate tests from each other with unique data first; Respawn or rollback if you must.
- Keep them few: workflows, auth, concurrency, serialisation. Validation belongs in unit tests.

**Code:** [`LogiFlowApiFactory.cs`](../../tests/LogiFlow.Api.IntegrationTests/LogiFlowApiFactory.cs) ·
[`OrderLifecycleTests.cs`](../../tests/LogiFlow.Api.IntegrationTests/OrderLifecycleTests.cs)

**Next:** [5. Architecture tests](05-architecture-tests.md)
