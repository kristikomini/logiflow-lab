# 3. Application tests — mock what you cannot control

> Part of [Module 12 — Testing](README.md), section 3.
> Previous: [2. Domain tests](README.md#2-domain-tests--the-ones-that-should-be-effortless) ·
> Next: [4. Integration tests](03-integration-testing.md)

---

Domain tests are easy: `Order` has no dependencies, so you construct one and assert. Handlers are the
first place a test has to make a decision, because a handler exists precisely to **coordinate things
it does not own** — repositories, a clock, a number generator, another service.

The decision is what to substitute, and there is one rule:

> **Mock what you cannot control. Use the real thing for everything you own.**

Databases, clocks, networks, message brokers, random numbers — substitute those. Your own entities
and value objects — never.

## The shape

```csharp
public sealed class CreateOrderHandlerTests
{
    private readonly ICustomerRepository _customers = Substitute.For<ICustomerRepository>();
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IOrderNumberGenerator _numbers = Substitute.For<IOrderNumberGenerator>();
    private readonly CreateOrderCommandHandler _handler;

    public CreateOrderHandlerTests()
    {
        _numbers.NextAsync(Arg.Any<CancellationToken>())
            .Returns(OrderNumber.Create(2026, 1).Value);

        _handler = new CreateOrderCommandHandler(_customers, _orders, _numbers);
    }
}
```

This repository uses **NSubstitute**; Moq is the other common choice and the concepts map one to one
(`Mock<T>.Object` ≈ `Substitute.For<T>()`, `Setup(...).Returns(...)` ≈ `.Returns(...)`,
`Verify(...)` ≈ `.Received()`). Job adverts name both; knowing one is enough.

**The constructor is the setup.** xUnit creates a new instance of the test class for **every test**,
so there is no shared state to leak between them and no `[SetUp]`/`[TearDown]` needed. That is a real
difference from NUnit and a good answer to "why xUnit?".

## The test that shows the rule

```csharp
[Fact]
public async Task Fails_when_the_customer_is_inactive()
{
    // A REAL Customer, deactivated. Not a mocked one — the rule under test is inside this object.
    Customer customer = ACustomer(active: false);
    _customers.GetByIdAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);

    Result result = await _handler.HandleAsync(ACommandFor(customer), default);

    result.Error.ShouldBe(OrderErrors.CustomerInactive);
    await _orders.DidNotReceive().AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
}
```

Three things are deliberate.

**The `Customer` is real.** The repository is mocked because it is I/O; the aggregate is not, because
the behaviour being tested lives inside it. Mock the `Customer` and you are testing that the handler
called the method you expected — which passes forever, including when the rule is broken.

**Asserting the *absence* of a side effect is often the real test.** `DidNotReceive().AddAsync(...)`
is the assertion that matters here: not just that an error came back, but that nothing was written.
A handler that returns a failure *and* saves is a bug no result-based assertion catches.

**The error is compared by identity**, not by message. `OrderErrors.CustomerInactive` is a value
([module 05 section 4](../module-05-clean-architecture/05-result-vs-exceptions.md)), so the assertion
is exact and a reworded description does not break the test.

## Helper builders, not shared fixtures

```csharp
private static Customer ACustomer(CustomerTier tier = CustomerTier.Gold, bool active = true)
{
    Customer customer = Customer.Create(
        "Rossi Logistica SRL",
        EmailAddress.Create("ops@rossi.it").Value,
        Address.Create("Via Roma 1", null, "Milano", null, "20100", "IT").Value,
        tier).Value;

    if (!active) customer.Deactivate();
    return customer;
}
```

Named `ACustomer`, with **defaults for everything and parameters for what the test cares about**. The
test then reads as one sentence — `ACustomer(active: false)` — and the twelve irrelevant fields stay
out of the way.

The alternative, a shared `TestData.ValidCustomer` static, couples every test to one object: change
it for one test and others fail for reasons unrelated to what they assert.

## What to assert, and what not to

**Assert outcomes and state.** Did it return the right error? Was the right thing added? Did the
aggregate end in the right status?

**Do not assert call order or exact call counts** unless the order genuinely is the behaviour. Those
tests break on every harmless refactor, everyone learns to "just update the test", and the suite
stops meaning anything.

**Do not mock what you can construct.** If a dependency is a pure function or a simple value object,
use the real one — the substitute adds setup and removes coverage.

**Prefer a fake to a mock for stateful collaborators.** An in-memory repository backed by a
`Dictionary` is often clearer than four `Returns` calls, and it catches "added then read back"
behaviour that mocks cannot.

## The clock, and why it must be injected

```csharp
public sealed class SubmitOrderHandler(IOrderRepository orders, TimeProvider clock)
```

`DateTimeOffset.UtcNow` inside a handler is an untestable dependency. Anything about expiry,
"orders from yesterday", or a
[shift crossing midnight](../../site/chapters/32-industrial-and-mes.html) becomes either flaky or
impossible to test.

`TimeProvider` (.NET 8+) is the framework's answer, and `FakeTimeProvider` from
`Microsoft.Extensions.TimeProvider.Testing` lets a test set the clock and advance it. Treat the clock
as I/O, because that is what it is.

## The mistakes

**Mocking your own domain.** `Substitute.For<Order>()` then `order.Received().Submit()`. Passes while
`Submit` is broken.

**Six mocks in one constructor.** Not a testing problem — the handler does too much. The test is
telling you something true about the design.

**Asserting on messages.** `result.Error.Description.ShouldContain("inactive")` breaks when somebody
improves the wording.

**Testing the mock.** `_orders.Received().AddAsync(...)` as the *only* assertion verifies that you
wrote the handler you wrote.

**No test for the failure paths.** The happy path is the one that already works. Every `return
SomeError` line in the handler deserves a test — and they are the cheap ones.

## Try it

```bash
dotnet test tests/LogiFlow.Application.Tests
```

Eight tests, in a few hundred milliseconds, with no Docker and no database. Then open
[`CreateOrderHandlerTests.cs`](../../tests/LogiFlow.Application.Tests/Orders/CreateOrderHandlerTests.cs)
and try replacing `ACustomer(active: false)` with a substituted `Customer`. You will find you have to
mock `Deactivate`, then mock whatever `Order.Create` reads from it, and the test ends up asserting
your own scaffolding.

Then break a rule inside `Customer` and confirm these tests go red — proving they test the domain
through the handler rather than testing the handler's memory of the domain.

## What to remember

- Mock what you cannot control; use the real thing for what you own.
- xUnit constructs the test class per test, so the constructor is the setup and state cannot leak.
- Keep aggregates and value objects real — the rules under test live inside them.
- Assert outcomes and the absence of side effects, never call order.
- Compare errors by identity, not by message.
- Builder helpers with defaults beat shared static fixtures.
- Inject `TimeProvider`; the clock is I/O.
- Six mocks means the handler does too much.

**Code:** [`CreateOrderHandlerTests.cs`](../../tests/LogiFlow.Application.Tests/Orders/CreateOrderHandlerTests.cs) ·
[`Features/Orders/CreateOrder.cs`](../../src/LogiFlow.Application/Features/Orders/CreateOrder.cs)

**Next:** [4. Integration tests](03-integration-testing.md)
