using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace LogiFlow.Api.RealTime;

/// <summary>
/// What the server can call on a connected client. One method per thing that can happen.
/// </summary>
/// <remarks>
/// <para>
/// A <b>strongly-typed hub</b> (<c>Hub&lt;T&gt;</c>) instead of the stringly-typed
/// <c>Clients.All.SendAsync("OrderStatusChanged", ...)</c>. The string version compiles no matter
/// what you type, so a renamed method or a reordered argument is found by a JavaScript developer,
/// at runtime, in a browser. This version is a compile error. The client still calls it by name —
/// the contract is not enforced across the wire and cannot be — but at least both ends now have
/// one place to read it from.
/// </para>
/// </remarks>
public interface IOrderTrackingClient
{
    /// <summary>An order moved to a new state.</summary>
    /// <param name="update">What changed.</param>
    /// <returns>A task that completes when the frame is written.</returns>
    Task OrderStatusChanged(OrderStatusUpdate update);
}

/// <summary>One order-status push.</summary>
/// <param name="OrderId">Which order.</param>
/// <param name="OrderNumber">The human-facing reference, so the UI need not fetch it.</param>
/// <param name="Status">Submitted, Confirmed, Shipped, Delivered, Cancelled.</param>
/// <param name="OccurredAtUtc">When the domain event was raised — not when it was sent.</param>
/// <remarks>
/// <para>
/// <b>A DTO, deliberately, and not the domain event.</b> Pushing <c>OrderSubmittedDomainEvent</c>
/// straight down the wire would put <c>Money</c>, <c>Weight</c> and the customer's
/// <c>Address</c> into a browser that asked for a status string — and would make every future
/// change to that record a breaking change for a client you cannot deploy. The projection is the
/// boundary.
/// </para>
/// <para>
/// <c>OccurredAtUtc</c> is the event's time, not <c>DateTimeOffset.UtcNow</c> at send. The outbox
/// polls every couple of seconds and can be minutes behind after an outage, so "when it happened"
/// and "when you were told" are genuinely different numbers. A UI that shows the second one as
/// the first is lying, quietly, in exactly the situation where someone is looking closely.
/// </para>
/// </remarks>
public sealed record OrderStatusUpdate(
    Guid OrderId,
    string OrderNumber,
    string Status,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// The live order-tracking hub. Clients subscribe to the orders they are looking at and receive
/// status changes as they are published by the outbox.
/// </summary>
/// <remarks>
/// <para>
/// <b>What SignalR actually is:</b> a negotiation over three transports — WebSockets, then
/// Server-Sent Events, then long polling — plus reconnection, plus a message protocol, plus
/// server-to-client method dispatch. On a modern network it is a WebSocket; the value is that you
/// do not write the other two paths for the network where it is not.
/// </para>
/// <para>
/// <b>Groups, not broadcast.</b> <c>Clients.All</c> is the demo and almost never the answer: a
/// warehouse screen watching order 41 has no business receiving order 42's shipping address.
/// Groups here are per order, joined explicitly, and they cost nothing when empty — a group is
/// just a name, created on first join and gone on last leave.
/// </para>
/// <para>
/// <b>Scaling out needs a backplane.</b> Connections live in one process's memory, so with two
/// instances behind a load balancer, an event handled by instance A reaches nobody connected to
/// instance B. The fix is the Redis backplane wired up in <c>Program.cs</c> — and it is worth
/// knowing that it is a *fan-out*, not a routing table: every instance receives every message and
/// discards what it has no connection for. That is fine at this size and is the reason SignalR
/// does not scale linearly forever.
/// </para>
/// <para>
/// <b>Authentication over a WebSocket</b> is the gotcha, and it is not solved in this file — see
/// the <c>OnMessageReceived</c> handler in <c>Infrastructure/Authentication.cs</c>. The browser
/// WebSocket API cannot set an <c>Authorization</c> header, so the token arrives as a query
/// string parameter and the JWT handler has to be told to look there.
/// </para>
/// Covered in: <c>course/module-25-distributed-systems/06-real-time.md</c>
/// </remarks>
[Authorize]
public sealed class OrderTrackingHub : Hub<IOrderTrackingClient>
{
    /// <summary>Builds the group name for one order.</summary>
    /// <param name="orderId">The order.</param>
    /// <returns>The group name both the hub and the publisher must agree on.</returns>
    /// <remarks>
    /// A method rather than two interpolated strings in two files. Group names are matched by
    /// ordinal string equality and a mismatch is silent: the publisher sends to a group nobody
    /// is in, the subscriber waits in a group nobody sends to, and neither logs anything.
    /// </remarks>
    public static string GroupFor(Guid orderId) =>
        string.Create(CultureInfo.InvariantCulture, $"order-{orderId}");

    /// <summary>Starts receiving updates for one order.</summary>
    /// <param name="orderId">The order to watch.</param>
    /// <returns>A task that completes once the connection has joined the group.</returns>
    public Task SubscribeToOrder(Guid orderId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(orderId), Context.ConnectionAborted);

    /// <summary>Stops receiving updates for one order.</summary>
    /// <param name="orderId">The order to stop watching.</param>
    /// <returns>A task that completes once the connection has left the group.</returns>
    /// <remarks>
    /// Leaving on disconnect is automatic — SignalR removes a dropped connection from every group
    /// it was in. This exists for the other case: a user navigating from one order to another
    /// inside a long-lived connection, who would otherwise accumulate subscriptions for the whole
    /// session.
    /// </remarks>
    public Task UnsubscribeFromOrder(Guid orderId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupFor(orderId), Context.ConnectionAborted);
}
