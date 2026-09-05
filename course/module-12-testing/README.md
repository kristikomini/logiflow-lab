# Module 12 — Testing

> Four suites, 54 tests, all green. This module is about what to test where, and why the
> in-memory database provider is a trap.

```bash
dotnet test        # all four suites
```

---

## Deeper chapters

Some sections below have a chapter that goes further — the code, the traps, and the interview answer.

| | Chapter | |
|---|---|---|
| 3 | [Application tests — mock what you cannot control](02-unit-testing-handlers.md) | what to substitute, and what never to |
| 4 | [Integration tests](03-integration-testing.md) | a throwaway database, and why not the in-memory provider |
| 5 | [Architecture tests](05-architecture-tests.md) | turning a convention into a build failure |

---

## 1. The pyramid, as built here

```
       ╱ 8  ╲        Integration — real HTTP, real SQL Server. Slow, high confidence.
      ╱──────╲
     ╱   16   ╲      Application + Architecture — handlers with mocks; the dependency graph.
    ╱──────────╲
   ╱     30     ╲    Domain — no mocks, no I/O, ~360ms for all of them.
  ╱──────────────╲
```

Many fast tests at the bottom, few slow ones at the top. Invert it and your suite takes 40
minutes, so people stop running it, so it stops catching anything.

---

## 2. Domain tests — the ones that should be effortless

📂 [`tests/LogiFlow.Domain.Tests/Orders/OrderTests.cs`](../../tests/LogiFlow.Domain.Tests/Orders/OrderTests.cs)

**Notice what is missing: mocks, a database, `async`, and any setup at all.**

```csharp
[Fact]
public void AddLine_WithProductAlreadyOnOrder_MergesInsteadOfDuplicating()
{
    Order order = ADraft();
    Product product = AProduct();

    order.AddLine(product, 3);
    order.AddLine(product, 2);

    order.Lines.Count.ShouldBe(1);
    order.Lines[0].Quantity.ShouldBe(5);
}
```

That is the payoff of a dependency-free Domain layer. 30 tests in ~360ms, and they can never fail
for an environmental reason — so when one goes red, a rule actually broke.

**Naming: `Method_Scenario_ExpectedOutcome`.** A failing test name should tell you what broke
without opening the file.

**Fixtures are plain helper methods**, not a constructor that runs for every test. Each test
states the data it depends on, so you can read one in isolation without scrolling up to find what
`_order` was.

### `[Theory]` for the same rule with different inputs

```csharp
[Theory]
[InlineData(CustomerTier.Standard, 0)]
[InlineData(CustomerTier.Silver, 5)]
[InlineData(CustomerTier.Gold, 10)]
[InlineData(CustomerTier.Platinum, 15)]
public void DiscountAmount_FollowsTheCustomerTier(CustomerTier tier, int expectedPercent)
```

Four tests, one method, and adding a tier is one line.

---

## 3. Application tests — mock what you cannot control

📂 [`tests/LogiFlow.Application.Tests/Orders/CreateOrderHandlerTests.cs`](../../tests/LogiFlow.Application.Tests/Orders/CreateOrderHandlerTests.cs)

**The rule: mock I/O; use the real thing for everything you own.**

```
                 ┌─────────────── code you own ──────────────────┐
   HTTP   ─────► │  handler       Order · Money · Specification  │ ─────►  SQL Server
   mock it       │  (real)        (real — NEVER mock these)      │         (a real database,
   clock, mail,  └──────────────────────────────────────────────┘          not the in-memory one)
   message bus
   mock them

   mock what you do not control; use the real thing for everything you do.
   Mock your own domain objects and the test asserts that the handler called the methods you
   expected — which passes happily while the business rule underneath is broken.
```

Repositories and the number generator are mocked. `Order` and `Customer` are **real**.

Mocking your own domain objects is a well-known anti-pattern — you end up asserting that the
handler called the methods you expected rather than that the right thing happened, and the test
passes happily while the business rule is broken.

### Assert the absence of side effects too

```csharp
_orders.DidNotReceive().Add(Arg.Any<Order>());
```

"It failed" is half the assertion. "…and did not stage anything for insertion" is the other half.

### Testing a design property

```csharp
typeof(CreateOrderCommandHandler).GetConstructors()[0].GetParameters()
    .ShouldNotContain(p => p.ParameterType == typeof(IUnitOfWork));
```

The handler must never save by itself — `TransactionBehavior` owns that. This documents it as
intentional rather than accidental.

---

## 4. Integration tests — and why not the in-memory provider

📂 [`tests/LogiFlow.Api.IntegrationTests/`](../../tests/LogiFlow.Api.IntegrationTests/)

**`UseInMemoryDatabase` is not a database.** It is a LINQ-to-Objects shim wearing a `DbContext`
costume:

| It cannot | So this passes in test and fails in production |
|---|---|
| enforce a schema | a 500-char string into `nvarchar(20)` |
| enforce unique indexes | duplicate SKUs |
| enforce CHECK constraints | `QuantityReserved > QuantityOnHand` |
| do transactions | a rollback that does not roll back |
| do `rowversion` | concurrency conflicts never detected |
| execute raw SQL | `NEXT VALUE FOR` untested |

Microsoft's own documentation now recommends against it. The suite here uses **a real SQL Server**
instead, giving every run its own throwaway database:

```csharp
// A fresh database per run - the GUID is the isolation.
private readonly string _database = $"LogiFlow_Test_{Guid.NewGuid():N}";

private const string DefaultServer =
    "Server=localhost;Integrated Security=True;TrustServerCertificate=True;Encrypt=True";
```

`MigrateAsync` creates that database and builds the schema from the real migrations;
`EnsureDeletedAsync` drops it in teardown. Set `LOGIFLOW_TEST_SQL` to point the suite at a different
server — a named instance, LocalDB, a build agent, or a container you started yourself.

`WebApplicationFactory<Program>` runs the **real** `Program.cs` — same DI, same middleware, same
endpoints — and only the connection string is swapped. Nothing else is stubbed, so a failing
integration test means the application is genuinely broken.

### Assert against the database, not the API

```csharp
await factory.WithDbContextAsync(async db =>
{
    Order? order = await db.Orders.FirstOrDefaultAsync(o => o.Id == typedId);
    order.Status.ShouldBe(OrderStatus.Submitted);
    order.FulfillingWarehouseId.ShouldNotBeNull();      // the domain event fired

    StockItem? stock = await db.StockItems.FirstOrDefaultAsync(...);
    stock.QuantityReserved.ShouldBe(5);                 // and reserved stock

    (await db.OutboxMessages.AnyAsync(m => m.Type.Contains("OrderSubmitted"))).ShouldBeTrue();
});
```

Asking the API whether the API worked is circular. Reading the row proves the event fired, the
interceptor ran, and the write committed.

### The test that justifies the whole architecture

```csharp
[Fact]
public async Task Insufficient_stock_rolls_the_whole_submission_back()
```

Stock is 2, the order wants 10. The reservation handler throws, `SaveChanges` aborts, and the
order **must still be Draft**. If that ever reads `Submitted`, the transaction boundary is broken
and you have orders promising stock that does not exist.

### Declare response shapes separately

The test project declares its own `OrderDetailResponse` rather than reusing the Application DTO.
A test sharing the production type **cannot detect a breaking change to the wire contract** —
rename a property and both sides move together, silently breaking every real client.

### Two xUnit mechanics worth knowing

**Explicit interface implementation** resolves a real clash: xUnit v2's `IAsyncLifetime` returns
`Task`, while `WebApplicationFactory` already has `ValueTask DisposeAsync()`. Two members, same
name, different return types → `CS0738`. Explicit implementation gives each contract its own
method.

**`ICollectionFixture`** shares one container across test classes. Without it xUnit creates a
fresh fixture — and a fresh SQL Server — per class.

---

## 5. Architecture tests

📂 [`tests/LogiFlow.ArchitectureTests/LayeringTests.cs`](../../tests/LogiFlow.ArchitectureTests/LayeringTests.cs)

Tests over the **dependency graph** rather than behaviour. Clean Architecture's rules are
conventions, and conventions decay: someone adds `using Microsoft.EntityFrameworkCore;` to a
domain class to "just add a quick query", the reviewer is busy, it merges. Six months later the
Domain cannot be tested without a database.

```csharp
[Fact]
public void Domain_DependsOnNothingButTheBcl()
```

Cheap insurance — a handful of tests, milliseconds, and they keep the design honest for years.

They also enforce conventions: handlers must be `internal` and `sealed`, aggregates `sealed`,
domain events records named in the past tense, commands and queries records.

**Make failures actionable.** The default assertion says `Expected: True, Actual: False`, which
tells you a rule broke but not which type broke it. `ArchTestMessages.FailureMessage` names the
offenders — a test that is annoying to diagnose is a test people eventually delete.

---

## 6. What not to test

- **Framework code.** Do not test that EF saves or that ASP.NET routes.
- **Getters and setters.** No behaviour, no test.
- **Implementation details.** Test that submitting reserves stock; do not test that it called
  `Reserve` exactly once with those arguments. The first survives a refactor, the second breaks.
- **Chasing 100% coverage.** Coverage measures lines executed, not assertions made. 100% coverage
  with weak assertions is worse than 70% with strong ones, because it feels safe.

---

## 7. Do this

1. Open `Domain/Orders/Order.cs` and delete the `if (!IsEditable)` guard in `AddLine`.
   Run `dotnet test tests/LogiFlow.Domain.Tests`. Which test fails? Would an integration test
   have caught it? `git checkout .`
2. Add a `using Microsoft.EntityFrameworkCore;` to any file in `LogiFlow.Domain` and run
   `dotnet test tests/LogiFlow.ArchitectureTests`.
3. Write a missing test: nothing currently asserts that `ReportingQueries` excludes cancelled
   orders from revenue. Write it — and notice that the gap existed.

---

## 8. Golden rules

> The card.

1. **Many fast tests, few slow ones.** Invert the pyramid and the suite takes forty minutes, so
   nobody runs it, so it catches nothing.
2. **A domain test needs no mocks, no database and no `async`.** If yours does, the design is
   telling you something.
3. **Mock what you do not control; use the real thing for what you own.** Never mock your own
   domain objects.
4. **Name tests `Method_Scenario_ExpectedOutcome`.** A failure should be diagnosable from the name
   alone, without opening the file.
5. **Assert the absence of side effects too.** "It failed" is half the assertion; "…and staged
   nothing for insertion" is the other half.
6. **`UseInMemoryDatabase` is not a database.** No schema, no unique indexes, no CHECK
   constraints, no transactions, no `rowversion`, no raw SQL — so it passes tests that production
   fails. Use a real SQL Server and give each run its own throwaway database.
7. **Assert against the database, not the API.** Asking the API whether the API worked is
   circular.
8. **Declare wire contracts separately in the test project.** A test that shares the production
   DTO cannot detect a breaking change to the contract.
9. **Test outcomes, not interactions.** "Submitting reserves stock" survives a refactor; "it
   called `Reserve` once with those arguments" breaks on the next one.
10. **Turn conventions into build failures.** Architecture tests cost milliseconds and keep the
    design honest through years of team turnover — but make the failure message name the
    offender, or people will delete the test.
11. **Do not chase coverage.** 100% with weak assertions is worse than 70% with strong ones,
    because it feels safe.

---

## 9. Interview questions

**"How do you test a service that hits a database?"**
Split it. Business rules go in a layer with no I/O and are unit tested with no mocks. Data access
is tested against a real database in a container. Avoid the in-memory provider — it has no
schema, constraints or transactions, so it passes tests that production fails.

**"What do you mock?"**
Things you do not control: databases, HTTP, clocks, message brokers. Never your own domain
objects — that tests the interaction rather than the outcome.

**"Unit vs integration — where do you draw the line?"**
Unit tests cover a single unit with no I/O; integration tests cover several real components
together. Many of the first, few of the second. If you cannot unit test something, that is
usually a design signal.

**"What is an architecture test?"**
A test over structure rather than behaviour — "the Domain must not reference EF Core", "handlers
must be internal". It turns a convention into a build failure so it survives team turnover.

**"What coverage do you aim for?"**
Push back gently. Coverage is a proxy, and a bad one. Aim for high coverage of *business rules*
and accept low coverage of DTOs and wiring. 100% with weak assertions is worse than 70% with
strong ones.

---

## Next

→ [Module 13 — Deployment](../module-13-deployment/)
