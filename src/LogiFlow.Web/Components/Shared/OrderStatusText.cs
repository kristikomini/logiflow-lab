using LogiFlow.Web.Contracts;

namespace LogiFlow.Web.Components.Shared;

/// <summary>
/// Human wording for <see cref="OrderStatus"/>: one sentence a six-year-old could follow, plus
/// whether the state is still moving.
/// </summary>
/// <remarks>
/// <para>
/// One table, used by the pill's tooltip, the lifecycle timeline and the list's colour legend. If
/// each of those wrote its own sentence they would drift apart within a month, and a UI that
/// describes the same state three different ways teaches the user that none of the descriptions
/// can be trusted.
/// </para>
/// <para>
/// <b>Wording only.</b> There is not a single rule in here. What an order may do next comes from
/// <c>OrderDetail.AllowedTransitions</c>, computed by the domain's state machine - see the note on
/// that property. This class must never grow a "can it be cancelled?" method.
/// </para>
/// </remarks>
public static class OrderStatusText
{
    /// <summary>The state, explained with no jargon at all.</summary>
    /// <param name="status">The state to describe.</param>
    /// <returns>A single plain sentence.</returns>
    public static string Kid(OrderStatus status) => status switch
    {
        OrderStatus.Draft => "Still being written, like a shopping list stuck on the fridge. Nobody has promised anything yet.",
        OrderStatus.Submitted => "Sent. The warehouse has been asked to put these things to one side.",
        OrderStatus.Confirmed => "The things really are on the shelf and the money is fine. This is happening.",
        OrderStatus.Shipped => "In a van. It has left the building and nobody can call it back.",
        OrderStatus.Delivered => "It arrived. The end of the story - nothing changes after this.",
        OrderStatus.Cancelled => "Called off before it was sent. Also the end of the story.",
        _ => "A state this screen was not taught about. Somebody added one and forgot this file.",
    };

    /// <summary>The same state in the words the warehouse would use.</summary>
    /// <param name="status">The state to describe.</param>
    /// <returns>A short operational description.</returns>
    public static string Plain(OrderStatus status) => status switch
    {
        OrderStatus.Draft => "Being assembled. Lines can still be added or removed.",
        OrderStatus.Submitted => "Placed. Awaiting stock reservation and payment authorisation.",
        OrderStatus.Confirmed => "Stock reserved, payment authorised.",
        OrderStatus.Shipped => "Handed to a carrier.",
        OrderStatus.Delivered => "Delivery confirmed. Terminal state.",
        OrderStatus.Cancelled => "Cancelled before shipping. Terminal state.",
        _ => "Unknown state.",
    };

    /// <summary>
    /// True while the order is still travelling - i.e. something outside this screen is expected
    /// to change it.
    /// </summary>
    /// <param name="status">The state to test.</param>
    /// <returns>False for <see cref="OrderStatus.Draft"/> and for the two terminal states.</returns>
    /// <remarks>
    /// Drives the pulsing dot on the pill. Draft is excluded on purpose: a draft is not in motion,
    /// it is sitting on somebody's desk waiting for a human.
    /// </remarks>
    public static bool IsMoving(OrderStatus status) =>
        status is OrderStatus.Submitted or OrderStatus.Confirmed or OrderStatus.Shipped;
}
