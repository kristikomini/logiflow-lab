using LogiFlow.Domain.Catalog;
using LogiFlow.Domain.Customers;
using LogiFlow.Domain.Orders;
using LogiFlow.Domain.Orders.Events;
using LogiFlow.Domain.Results;
using LogiFlow.Domain.ValueObjects;

namespace LogiFlow.Domain.Tests.Orders;

/// <summary>
/// Behavioural tests for the <see cref="Order"/> aggregate.
/// </summary>
/// <remarks>
/// <para>
/// <b>Notice what is missing: mocks, a database, async, and any setup at all.</b> That is the
/// whole payoff of keeping business rules in a dependency-free Domain layer. These tests run in
/// single-digit milliseconds and can never fail for an environmental reason, which means when
/// one goes red you know a rule actually broke.
/// </para>
/// <para>
/// <b>Naming convention:</b> <c>Method_Scenario_ExpectedOutcome</c>. A failing test name should
/// tell you what broke without opening the file — that is the difference between a test suite
/// that helps at 2am and one that just says <c>Test17 failed</c>.
/// </para>
/// </remarks>
public sealed class OrderTests
{
    // ── Fixtures ────────────────────────────────────────────────────────────────────────
    // Plain helper methods, not a [SetUp] / constructor that runs for every test. Each test
    // states exactly the data it depends on, so you can read one test in isolation and
    // understand it - no scrolling up to find what `_order` was initialised to.

    private static Product AProduct(decimal price = 100m, int grams = 500, bool active = true)
    {
        Product product = Product.Create(
            Sku.Create("ELE-100001").Value,
            "Test Product",
            null,
            new Money(price, Currency.Eur),
            Weight.FromGrams(grams).Value).Value;

        if (!active)
        {
            product.Discontinue();
        }

        return product;
    }

    private static Order ADraft(CustomerTier tier = CustomerTier.Standard) =>
        Order.CreateDraft(
            OrderNumber.Create(2026, 1).Value,
            CustomerId.New(),
            tier,
            Currency.Eur,
            AnAddress()).Value;

    private static Address AnAddress() =>
        Address.Create("Via Roma 1", null, "Milano", null, "20100", "IT").Value;

    // ── Creation ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CreateDraft_WithValidData_StartsInDraftStatus()
    {
        Order order = ADraft();

        order.Status.ShouldBe(OrderStatus.Draft);
        order.Lines.ShouldBeEmpty();
        order.IsEditable.ShouldBeTrue();
        order.SubmittedAtUtc.ShouldBeNull();
    }

    [Fact]
    public void CreateDraft_WithEmptyCustomerId_Fails()
    {
        Result<Order> result = Order.CreateDraft(
            OrderNumber.Create(2026, 1).Value,
            CustomerId.From(Guid.Empty),
            CustomerTier.Standard,
            Currency.Eur);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(OrderErrors.CustomerRequired);
    }

    // ── Lines ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AddLine_WithNewProduct_AddsALine()
    {
        Order order = ADraft();
        Product product = AProduct(price: 50m);

        Result result = order.AddLine(product, 3);

        result.IsSuccess.ShouldBeTrue();
        order.Lines.Count.ShouldBe(1);
        order.Lines[0].Quantity.ShouldBe(3);
        order.Lines[0].UnitPrice.Amount.ShouldBe(50m);
    }

    /// <summary>
    /// The merge rule is a documented business decision, so it gets a test that would fail
    /// loudly if someone "simplified" AddLine into an unconditional append.
    /// </summary>
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

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void AddLine_WithNonPositiveQuantity_Fails(int quantity)
    {
        Order order = ADraft();

        Result result = order.AddLine(AProduct(), quantity);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(OrderErrors.InvalidQuantity);
    }

    [Fact]
    public void AddLine_WithDiscontinuedProduct_Fails()
    {
        Order order = ADraft();
        Product discontinued = AProduct(active: false);

        Result result = order.AddLine(discontinued, 1);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Product.Inactive");
    }

    /// <summary>
    /// Guards the invariant that every line shares the order's currency — the thing that makes
    /// <c>Subtotal</c>'s <c>Money</c> arithmetic safe.
    /// </summary>
    [Fact]
    public void AddLine_WithMismatchedCurrency_Fails()
    {
        Order order = ADraft();

        Product usdProduct = Product.Create(
            Sku.Create("ELE-100002").Value,
            "Dollar Product",
            null,
            new Money(100m, Currency.Usd),
            Weight.FromGrams(100).Value).Value;

        Result result = order.AddLine(usdProduct, 1);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Order.CurrencyMismatch");
    }

    [Fact]
    public void ChangeLineQuantity_ToZero_RemovesTheLine()
    {
        Order order = ADraft();
        order.AddLine(AProduct(), 5);
        OrderLineId lineId = order.Lines[0].Id;

        Result result = order.ChangeLineQuantity(lineId, 0);

        result.IsSuccess.ShouldBeTrue();
        order.Lines.ShouldBeEmpty();
    }

    /// <summary>
    /// The encapsulation test. If this ever fails to compile it is because someone exposed the
    /// backing list, and every invariant on this aggregate became optional.
    /// </summary>
    [Fact]
    public void Lines_IsNotDirectlyMutable()
    {
        Order order = ADraft();

        order.Lines.ShouldBeAssignableTo<IReadOnlyList<OrderLine>>();
        typeof(Order).GetProperty(nameof(Order.Lines))!.CanWrite.ShouldBeFalse();
    }

    // ── Money ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Subtotal_SumsEveryLine()
    {
        Order order = ADraft();
        Product a = AProduct(price: 10m);

        Product b = Product.Create(
            Sku.Create("PAC-200001").Value, "B", null,
            new Money(25m, Currency.Eur), Weight.FromGrams(100).Value).Value;

        order.AddLine(a, 3);   //  30
        order.AddLine(b, 2);   // +50

        order.Subtotal.Amount.ShouldBe(80m);
    }

    [Fact]
    public void Subtotal_OnAnEmptyOrder_IsZeroInTheOrdersCurrency()
    {
        // Guards the Aggregate() seed. A seedless Sum would throw on an empty sequence,
        // and a naive `new Money(0, Currency.Eur)` would be wrong for a USD order.
        Order order = ADraft();

        order.Subtotal.Amount.ShouldBe(0m);
        order.Subtotal.Currency.ShouldBe(Currency.Eur);
    }

    [Theory]
    [InlineData(CustomerTier.Standard, 0)]
    [InlineData(CustomerTier.Silver, 5)]
    [InlineData(CustomerTier.Gold, 10)]
    [InlineData(CustomerTier.Platinum, 15)]
    public void DiscountAmount_FollowsTheCustomerTier(CustomerTier tier, int expectedPercent)
    {
        Order order = ADraft(tier);
        order.AddLine(AProduct(price: 100m), 10);   // subtotal 1000

        order.DiscountAmount.Amount.ShouldBe(expectedPercent * 10m);
    }

    [Fact]
    public void ShippingCost_IsFreeOnceTheTierThresholdIsMet()
    {
        // Standard tier: free shipping above EUR 150.
        Order order = ADraft(CustomerTier.Standard);
        order.AddLine(AProduct(price: 200m, grams: 100), 1);

        order.ShippingCost.IsZero.ShouldBeTrue();
    }

    [Fact]
    public void ShippingCost_IsChargedBelowTheThreshold()
    {
        Order order = ADraft(CustomerTier.Standard);
        order.AddLine(AProduct(price: 10m, grams: 100), 1);

        order.ShippingCost.Amount.ShouldBe(4.90m);
    }

    [Fact]
    public void ShippingCost_AddsAnExcessWeightCharge()
    {
        // 8kg total: 5kg free, 3kg excess at EUR 1.50/kg = 4.50, plus the 4.90 base.
        Order order = ADraft(CustomerTier.Standard);
        order.AddLine(AProduct(price: 10m, grams: 8000), 1);

        order.ShippingCost.Amount.ShouldBe(9.40m);
    }

    // ── State machine ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Submit_WithNoLines_Fails()
    {
        Order order = ADraft();

        Result result = order.Submit();

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(OrderErrors.EmptyOrder);
        order.Status.ShouldBe(OrderStatus.Draft);
    }

    [Fact]
    public void Submit_WithNoShippingAddress_Fails()
    {
        Order order = Order.CreateDraft(
            OrderNumber.Create(2026, 1).Value,
            CustomerId.New(),
            CustomerTier.Standard,
            Currency.Eur,
            shippingAddress: null).Value;

        order.AddLine(AProduct(), 1);

        Result result = order.Submit();

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(OrderErrors.ShippingAddressRequired);
    }

    [Fact]
    public void Submit_WithLinesAndAddress_Succeeds()
    {
        Order order = ADraft();
        order.AddLine(AProduct(), 1);

        Result result = order.Submit();

        result.IsSuccess.ShouldBeTrue();
        order.Status.ShouldBe(OrderStatus.Submitted);
        order.SubmittedAtUtc.ShouldNotBeNull();
        order.IsEditable.ShouldBeFalse();
    }

    [Fact]
    public void AddLine_AfterSubmission_Fails()
    {
        Order order = ADraft();
        order.AddLine(AProduct(), 1);
        order.Submit();

        Result result = order.AddLine(AProduct(), 1);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Order.NotEditable");
    }

    [Fact]
    public void Cancel_AfterShipping_Fails()
    {
        Order order = ADraft();
        order.AddLine(AProduct(), 1);
        order.Submit();
        order.Confirm();
        order.MarkShipped();

        Result result = order.Cancel("Too late");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Order.InvalidTransition");

        // The error should tell the caller what IS possible, not just say no.
        result.Error.Description.ShouldContain("Delivered");
    }

    [Fact]
    public void Cancel_WithoutAReason_Fails()
    {
        Order order = ADraft();
        order.AddLine(AProduct(), 1);
        order.Submit();

        Result result = order.Cancel("   ");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(OrderErrors.CancellationReasonRequired);
    }

    [Fact]
    public void FullLifecycle_DraftToDelivered_Succeeds()
    {
        Order order = ADraft();
        order.AddLine(AProduct(), 2);

        order.Submit().IsSuccess.ShouldBeTrue();
        order.Confirm().IsSuccess.ShouldBeTrue();
        order.MarkShipped().IsSuccess.ShouldBeTrue();
        order.MarkDelivered().IsSuccess.ShouldBeTrue();

        order.Status.ShouldBe(OrderStatus.Delivered);
        order.DeliveredAtUtc.ShouldNotBeNull();
        OrderStateMachine.IsTerminal(order.Status).ShouldBeTrue();
    }

    // ── Domain events ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Submit_RaisesOrderSubmittedCarryingTheTotal()
    {
        Order order = ADraft(CustomerTier.Gold);
        order.AddLine(AProduct(price: 100m), 10);   // 1000, minus 10% Gold = 900

        order.Submit();

        OrderSubmittedDomainEvent submitted = order.DomainEvents
            .OfType<OrderSubmittedDomainEvent>()
            .ShouldHaveSingleItem();

        submitted.Total.Amount.ShouldBe(900m);
        submitted.OrderId.ShouldBe(order.Id);
        submitted.ShippingAddress.City.ShouldBe("Milano");
    }

    [Fact]
    public void ClearDomainEvents_EmptiesTheCollection()
    {
        Order order = ADraft();
        order.AddLine(AProduct(), 1);
        order.DomainEvents.ShouldNotBeEmpty();

        order.ClearDomainEvents();

        order.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void AssignFulfillingWarehouse_ToADifferentSite_Fails()
    {
        Order order = ADraft();
        var first = Domain.Inventory.WarehouseId.New();
        var second = Domain.Inventory.WarehouseId.New();

        order.AssignFulfillingWarehouse(first).IsSuccess.ShouldBeTrue();

        // Re-assigning the SAME site is idempotent...
        order.AssignFulfillingWarehouse(first).IsSuccess.ShouldBeTrue();

        // ...but moving it elsewhere would strand the original reservation.
        Result result = order.AssignFulfillingWarehouse(second);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Order.AlreadyAssignedToWarehouse");
    }
}
