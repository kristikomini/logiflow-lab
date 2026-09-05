using LogiFlow.Domain.Inventory;
using LogiFlow.Domain.Results;
using LogiFlow.Domain.ValueObjects;

namespace LogiFlow.Domain.Orders;

/// <summary>
/// Every failure mode of the <see cref="Order"/> aggregate, in one place.
/// </summary>
/// <remarks>
/// Grouping errors per aggregate gives you a single screen that answers "what can go wrong
/// with an order?" — useful for API documentation, for front-end developers building error
/// handling, and for spotting in code review when someone invents a redundant failure mode.
/// Constants where the message is fixed; factory methods where it needs context.
/// </remarks>
public static class OrderErrors
{
    /// <summary>Order had no customer.</summary>
    public static readonly Error CustomerRequired =
        Error.Validation("Order.CustomerRequired", "An order must belong to a customer.");

    /// <summary>Quantity was zero or negative.</summary>
    public static readonly Error InvalidQuantity =
        Error.Validation("Order.InvalidQuantity", "Quantity must be at least 1.");

    /// <summary>Tried to submit an order with no lines.</summary>
    public static readonly Error EmptyOrder =
        Error.Validation("Order.Empty", "Cannot submit an order with no lines.");

    /// <summary>Tried to submit without a delivery address.</summary>
    public static readonly Error ShippingAddressRequired =
        Error.Validation("Order.ShippingAddressRequired", "A shipping address is required before submitting.");

    /// <summary>Cancelled without saying why.</summary>
    public static readonly Error CancellationReasonRequired =
        Error.Validation("Order.CancellationReasonRequired", "A cancellation reason is required.");

    /// <summary>Line limit exceeded.</summary>
    public static readonly Error TooManyLines =
        Error.Validation("Order.TooManyLines", $"An order cannot have more than {Order.MaxLines} lines.");

    /// <summary>No order exists with the requested id.</summary>
    public static Error NotFound(OrderId id) =>
        Error.NotFound("Order.NotFound", $"No order found with id '{id}'.");

    /// <summary>No order exists with the requested reference.</summary>
    public static Error NumberNotFound(OrderNumber number) =>
        Error.NotFound("Order.NumberNotFound", $"No order found with number '{number}'.");

    /// <summary>The requested line is not on this order.</summary>
    public static Error LineNotFound(OrderLineId id) =>
        Error.NotFound("Order.LineNotFound", $"No line found with id '{id}' on this order.");

    /// <summary>Tried to edit an order past the draft stage.</summary>
    public static Error NotEditable(OrderStatus status) =>
        Error.Conflict("Order.NotEditable", $"An order in status '{status}' can no longer be modified.");

    /// <summary>Attempted an illegal state change.</summary>
    /// <remarks>
    /// The message names the states that <i>are</i> reachable. An error that tells the caller
    /// what they can do next is worth five that only say "no".
    /// </remarks>
    public static Error InvalidTransition(OrderStatus from, OrderStatus to)
    {
        IReadOnlyList<OrderStatus> allowed = OrderStateMachine.NextStates(from);
        string options = allowed.Count == 0
            ? "it is in a terminal state"
            : $"allowed transitions are: {string.Join(", ", allowed)}";

        return Error.Conflict(
            "Order.InvalidTransition",
            $"Cannot move an order from '{from}' to '{to}' - {options}.");
    }

    /// <summary>Product currency did not match the order currency.</summary>
    public static Error CurrencyMismatch(Currency orderCurrency, Currency productCurrency) =>
        Error.Validation(
            "Order.CurrencyMismatch",
            $"This order is in {orderCurrency.Code}; the product is priced in {productCurrency.Code}.");

    /// <summary>Stock for this order is already reserved at a different site.</summary>
    public static Error AlreadyAssignedToWarehouse(WarehouseId warehouseId) =>
        Error.Conflict(
            "Order.AlreadyAssignedToWarehouse",
            $"Stock for this order is already reserved at warehouse '{warehouseId}'.");

    /// <summary>Another transaction modified the order first.</summary>
    public static readonly Error ConcurrencyConflict =
        Error.Conflict(
            "Order.ConcurrencyConflict",
            "This order was modified by someone else. Reload it and try again.");
}
