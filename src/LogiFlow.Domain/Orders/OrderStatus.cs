using System.Collections.Frozen;

namespace LogiFlow.Domain.Orders;

/// <summary>
/// Where an order sits in its lifecycle.
/// </summary>
/// <remarks>
/// Explicit numeric values because this is persisted — see the remarks on
/// <see cref="Customers.CustomerTier"/> for why that matters.
/// </remarks>
public enum OrderStatus
{
    /// <summary>Being assembled. Lines can be freely added and removed. Not visible to fulfilment.</summary>
    Draft = 1,

    /// <summary>Placed by the customer, awaiting stock reservation and payment authorisation.</summary>
    Submitted = 2,

    /// <summary>Stock reserved and payment authorised. Ready to pick and pack.</summary>
    Confirmed = 3,

    /// <summary>Handed to a carrier. A tracking number exists.</summary>
    Shipped = 4,

    /// <summary>Carrier confirmed delivery. Terminal state.</summary>
    Delivered = 5,

    /// <summary>Cancelled before shipping. Terminal state.</summary>
    Cancelled = 6,
}

/// <summary>
/// The order state machine, expressed as data rather than scattered <c>if</c> statements.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why centralise the transition table?</b> The alternative is a guard clause at the top of
/// every method — <c>if (Status != OrderStatus.Confirmed) return ...;</c> — repeated eight
/// times. That works right up until someone adds a state and updates seven of the eight.
/// A single table means the rules are reviewable in one screen, and testable exhaustively:
/// <c>OrderStateMachineTests</c> asserts over every (from, to) pair, all 36 of them.
/// </para>
/// <para>
/// A frozen dictionary is used because this is built once and read constantly.
/// <see cref="System.Collections.Frozen.FrozenDictionary{TKey,TValue}"/> (.NET 8) spends extra
/// time during construction to produce a faster, allocation-free lookup — precisely the right
/// trade for static configuration data. Benchmarked in <c>Labs.Benchmarks</c>.
/// </para>
/// Covered in: <c>course/module-05-clean-architecture/06-state-machines.md</c>
/// </remarks>
public static class OrderStateMachine
{
    private static readonly FrozenDictionary<OrderStatus, OrderStatus[]> Allowed =
        new Dictionary<OrderStatus, OrderStatus[]>
        {
            [OrderStatus.Draft] = [OrderStatus.Submitted, OrderStatus.Cancelled],
            [OrderStatus.Submitted] = [OrderStatus.Confirmed, OrderStatus.Cancelled],
            [OrderStatus.Confirmed] = [OrderStatus.Shipped, OrderStatus.Cancelled],

            // Once it is on a lorry, "cancel" is a returns process, not a state change.
            // Modelling that honestly here prevents an entire class of support ticket.
            [OrderStatus.Shipped] = [OrderStatus.Delivered],

            [OrderStatus.Delivered] = [],
            [OrderStatus.Cancelled] = [],
        }.ToFrozenDictionary();

    /// <summary>True when <paramref name="to"/> is a legal next state from <paramref name="from"/>.</summary>
    public static bool CanTransition(OrderStatus from, OrderStatus to) =>
        Allowed.TryGetValue(from, out OrderStatus[]? targets) && Array.IndexOf(targets, to) >= 0;

    /// <summary>The legal next states from <paramref name="from"/>. Empty for terminal states.</summary>
    public static IReadOnlyList<OrderStatus> NextStates(OrderStatus from) =>
        Allowed.TryGetValue(from, out OrderStatus[]? targets) ? targets : [];

    /// <summary>True when no further transitions are possible.</summary>
    public static bool IsTerminal(OrderStatus status) => NextStates(status).Count == 0;

    /// <summary>True while the order can still be edited by the customer.</summary>
    public static bool IsEditable(OrderStatus status) => status == OrderStatus.Draft;
}
