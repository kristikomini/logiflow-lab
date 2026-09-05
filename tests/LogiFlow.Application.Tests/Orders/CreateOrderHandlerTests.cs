using LogiFlow.Application.Abstractions.Data;
using LogiFlow.Application.Abstractions.Services;
using LogiFlow.Application.Features.Orders;
using LogiFlow.Domain.Customers;
using LogiFlow.Domain.Orders;
using LogiFlow.Domain.Results;
using LogiFlow.Domain.ValueObjects;

namespace LogiFlow.Application.Tests.Orders;

/// <summary>
/// Tests for the <c>CreateOrderCommand</c> handler.
/// </summary>
/// <remarks>
/// <para>
/// <b>What to mock, and what not to.</b> The repositories and the number generator are mocked —
/// they are I/O, and this test is not about I/O. The <see cref="Order"/> and
/// <see cref="Customer"/> aggregates are <i>real</i>. Mocking your own domain objects is a
/// well-known anti-pattern: you end up asserting that the handler called the methods you
/// expected rather than that the right thing happened, and the test passes happily while the
/// business rule is broken.
/// </para>
/// <para>
/// <b>The rule:</b> mock what you cannot control (databases, clocks, networks). Use the real
/// thing for everything you own.
/// </para>
/// Covered in: <c>course/module-12-testing/02-unit-testing-handlers.md</c>
/// </remarks>
public sealed class CreateOrderHandlerTests
{
    private readonly ICustomerRepository _customers = Substitute.For<ICustomerRepository>();
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IOrderNumberGenerator _numbers = Substitute.For<IOrderNumberGenerator>();
    private readonly CreateOrderCommandHandler _handler;

    /// <summary>Sets up the handler with mocked collaborators.</summary>
    public CreateOrderHandlerTests()
    {
        _numbers.NextAsync(Arg.Any<CancellationToken>())
            .Returns(OrderNumber.Create(2026, 1).Value);

        _handler = new CreateOrderCommandHandler(_customers, _orders, _numbers);
    }

    private static Customer ACustomer(CustomerTier tier = CustomerTier.Gold, bool active = true)
    {
        Customer customer = Customer.Create(
            "Rossi Logistica SRL",
            EmailAddress.Create("ops@rossi.it").Value,
            Address.Create("Via Roma 1", null, "Milano", null, "20100", "IT").Value,
            tier).Value;

        if (!active)
        {
            customer.Deactivate();
        }

        return customer;
    }

    private static AddressDto AnAddressDto() =>
        new("Via Roma 1", null, "Milano", null, "20100", "IT");

    [Fact]
    public async Task Returns_the_new_order_id_when_everything_is_valid()
    {
        Customer customer = ACustomer();
        _customers.GetAsync(Arg.Any<CustomerId>(), Arg.Any<CancellationToken>()).Returns(customer);

        var command = new CreateOrderCommand(customer.Id.Value, "EUR", AnAddressDto());

        Result<Guid> result = await _handler.HandleAsync(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task Copies_the_customers_tier_onto_the_order()
    {
        // Guards the "tier is captured at order time" rule. If the handler ever started looking
        // the tier up lazily, an upgrade would retroactively re-price historical orders.
        Customer customer = ACustomer(CustomerTier.Platinum);
        _customers.GetAsync(Arg.Any<CustomerId>(), Arg.Any<CancellationToken>()).Returns(customer);

        Order? captured = null;
        _orders.When(r => r.Add(Arg.Any<Order>())).Do(call => captured = call.Arg<Order>());

        await _handler.HandleAsync(
            new CreateOrderCommand(customer.Id.Value, "EUR", AnAddressDto()),
            CancellationToken.None);

        captured.ShouldNotBeNull();
        captured.CustomerTier.ShouldBe(CustomerTier.Platinum);
        captured.Status.ShouldBe(OrderStatus.Draft);
    }

    [Fact]
    public async Task Fails_with_NotFound_when_the_customer_does_not_exist()
    {
        _customers.GetAsync(Arg.Any<CustomerId>(), Arg.Any<CancellationToken>()).Returns((Customer?)null);

        Result<Guid> result = await _handler.HandleAsync(
            new CreateOrderCommand(Guid.CreateVersion7(), "EUR", AnAddressDto()),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Customer.NotFound");
        result.Error.Type.ShouldBe(ErrorType.NotFound);

        // Nothing was staged for insertion. Asserting the absence of a side effect is as
        // important as asserting the presence of one.
        _orders.DidNotReceive().Add(Arg.Any<Order>());
    }

    [Fact]
    public async Task Fails_when_the_customer_is_inactive()
    {
        Customer inactive = ACustomer(active: false);
        _customers.GetAsync(Arg.Any<CustomerId>(), Arg.Any<CancellationToken>()).Returns(inactive);

        Result<Guid> result = await _handler.HandleAsync(
            new CreateOrderCommand(inactive.Id.Value, "EUR", AnAddressDto()),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(CustomerErrors.Inactive);
        _orders.DidNotReceive().Add(Arg.Any<Order>());
    }

    [Fact]
    public async Task Fails_on_an_unsupported_currency_without_touching_the_database()
    {
        Result<Guid> result = await _handler.HandleAsync(
            new CreateOrderCommand(Guid.CreateVersion7(), "XYZ", AnAddressDto()),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Currency.Unsupported");

        // The currency is parsed FIRST, so a bad request costs zero database round trips.
        // Cheap checks before expensive ones is a habit worth having.
        await _customers.DidNotReceive().GetAsync(Arg.Any<CustomerId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Allows_a_draft_with_no_shipping_address()
    {
        // A draft may legitimately have no address yet; Submit() is what requires one.
        Customer customer = ACustomer();
        _customers.GetAsync(Arg.Any<CustomerId>(), Arg.Any<CancellationToken>()).Returns(customer);

        Result<Guid> result = await _handler.HandleAsync(
            new CreateOrderCommand(customer.Id.Value, "EUR", null),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Fails_when_the_supplied_address_is_incomplete()
    {
        Customer customer = ACustomer();
        _customers.GetAsync(Arg.Any<CustomerId>(), Arg.Any<CancellationToken>()).Returns(customer);

        var badAddress = new AddressDto("Via Roma 1", null, "", null, "20100", "IT");

        Result<Guid> result = await _handler.HandleAsync(
            new CreateOrderCommand(customer.Id.Value, "EUR", badAddress),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Address.CityRequired");
    }

    [Fact]
    public async Task Never_saves_by_itself()
    {
        // The handler stages changes; TransactionBehavior commits them. If a handler ever grew
        // its own SaveChangesAsync call, a later failure could no longer roll the whole use case
        // back. The handler has no IUnitOfWork dependency at all, which is the real guarantee -
        // this test documents that as an intentional design property rather than an oversight.
        Customer customer = ACustomer();
        _customers.GetAsync(Arg.Any<CustomerId>(), Arg.Any<CancellationToken>()).Returns(customer);

        await _handler.HandleAsync(
            new CreateOrderCommand(customer.Id.Value, "EUR", AnAddressDto()),
            CancellationToken.None);

        _orders.Received(1).Add(Arg.Any<Order>());

        typeof(CreateOrderCommandHandler)
            .GetConstructors()[0]
            .GetParameters()
            .ShouldNotContain(p => p.ParameterType == typeof(IUnitOfWork));
    }
}
