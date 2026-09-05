using LogiFlow.Api.RealTime;
using LogiFlow.Application.Abstractions.Messaging;
using LogiFlow.Domain.Catalog;
using LogiFlow.Domain.Common;
using LogiFlow.Domain.Customers;
using LogiFlow.Domain.Inventory;
using LogiFlow.Domain.Inventory.Events;
using LogiFlow.Domain.Orders;
using LogiFlow.Domain.Orders.Events;
using LogiFlow.Domain.ValueObjects;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace LogiFlow.Api.IntegrationTests;

/// <summary>
/// The real-time path, over a real connection.
/// </summary>
/// <remarks>
/// <para>
/// These connect a genuine SignalR client to the in-memory server, which is the only way to
/// cover the parts that actually break. A test that hands a mocked <c>IHubContext</c> to the
/// publisher proves the <c>switch</c> statement maps five event types — worth having, and it is
/// not where the bugs are. The bugs are in the handshake (the token cannot travel in a header),
/// in group naming (a mismatch is silent on both sides), and in serialization.
/// </para>
/// <para>
/// The publisher is invoked directly rather than by writing to the outbox, because the outbox
/// polls on a ten-second timer and a test that waits for it is a test nobody runs. What that
/// gives up is coverage of the outbox→publisher wiring, which
/// <c>OutboxProcessor</c>'s own tests cover from the other side.
/// </para>
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class OrderTrackingHubTests(LogiFlowApiFactory factory)
{
    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task A_subscriber_receives_the_status_change_for_its_own_order()
    {
        Guid orderId = Guid.CreateVersion7();

        await using HubConnection connection = await ConnectAsync();

        // The push arrives on a background thread, so the assertion needs somewhere to wait.
        // A TaskCompletionSource with a timeout, rather than a sleep: it returns the instant the
        // message lands and fails fast when it never does.
        TaskCompletionSource<OrderStatusUpdate> received = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<OrderStatusUpdate>(nameof(IOrderTrackingClient.OrderStatusChanged), update => received.TrySetResult(update));

        await connection.InvokeAsync("SubscribeToOrder", orderId);

        await PublishAsync(SubmittedEvent(orderId, 42));

        OrderStatusUpdate update = await received.Task.WaitAsync(ReceiveTimeout);

        update.OrderId.ShouldBe(orderId);
        update.OrderNumber.ShouldBe("ORD-2026-000042");
        update.Status.ShouldBe("Submitted");
    }

    [Fact]
    public async Task A_subscriber_hears_nothing_about_an_order_it_did_not_subscribe_to()
    {
        // The whole reason for groups. Without them every connected warehouse screen would
        // receive every customer's shipping address, and nobody would notice until an audit.
        await using HubConnection connection = await ConnectAsync();

        TaskCompletionSource<OrderStatusUpdate> received = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<OrderStatusUpdate>(nameof(IOrderTrackingClient.OrderStatusChanged), update => received.TrySetResult(update));

        await connection.InvokeAsync("SubscribeToOrder", Guid.CreateVersion7());

        await PublishAsync(SubmittedEvent(Guid.CreateVersion7(), 99));

        // A short wait, then assert nothing came. Proving a negative needs a deadline, and this
        // one is deliberately much shorter than ReceiveTimeout: the positive test above already
        // established how quickly a delivered message arrives.
        Task completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(1)));
        completed.ShouldNotBe(received.Task);
    }

    [Fact]
    public async Task Unsubscribing_stops_the_updates()
    {
        Guid orderId = Guid.CreateVersion7();

        await using HubConnection connection = await ConnectAsync();

        TaskCompletionSource<OrderStatusUpdate> received = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<OrderStatusUpdate>(nameof(IOrderTrackingClient.OrderStatusChanged), update => received.TrySetResult(update));

        await connection.InvokeAsync("SubscribeToOrder", orderId);
        await connection.InvokeAsync("UnsubscribeFromOrder", orderId);

        await PublishAsync(SubmittedEvent(orderId, 43));

        Task completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(1)));
        completed.ShouldNotBe(received.Task);
    }

    [Fact]
    public async Task An_anonymous_connection_is_refused()
    {
        // The hub carries [Authorize]. Without a token the handshake must fail — and it has to
        // fail at connect, not at the first invoke, or an unauthenticated client sits in a group
        // receiving other people's orders.
        await using HubConnection connection = BuildConnection(accessToken: null);

        await Should.ThrowAsync<Exception>(async () => await connection.StartAsync());
    }

    [Fact]
    public async Task An_event_nobody_is_watching_for_is_ignored_rather_than_failed()
    {
        // StockReceived has no browser waiting for it. The publisher must return quietly:
        // throwing would make the outbox count a delivery failure and eventually quarantine a
        // message that was never a problem.
        using IServiceScope scope = factory.Services.CreateScope();
        IIntegrationEventPublisher publisher = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();

        // Also asserts the API's registration won over Infrastructure's logging default.
        publisher.ShouldBeOfType<SignalRIntegrationEventPublisher>();

        await Should.NotThrowAsync(async () =>
            await publisher.PublishAsync(
                new StockReceivedDomainEvent(WarehouseId.New(), ProductId.New(), 10, 10),
                CancellationToken.None));
    }

    // ── Plumbing ─────────────────────────────────────────────────────────────────────────

    private static OrderSubmittedDomainEvent SubmittedEvent(Guid orderId, int sequence) =>
        new(
            new OrderId(orderId),
            OrderNumber.Create(2026, sequence).Value,
            CustomerId.New(),
            new Money(100m, Currency.Eur),
            Weight.FromGrams(500).Value,
            Address.Create("Via Roma 1", null, "Modena", null, "41100", "IT").Value);

    private async Task PublishAsync(IDomainEvent domainEvent)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        IIntegrationEventPublisher publisher = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();

        await publisher.PublishAsync(domainEvent, CancellationToken.None);
    }

    private async Task<HubConnection> ConnectAsync()
    {
        HubConnection connection = BuildConnection(await GetTokenAsync());
        await connection.StartAsync();
        return connection;
    }

    private HubConnection BuildConnection(string? accessToken) =>
        new HubConnectionBuilder()
            .WithUrl(
                new Uri(factory.Server.BaseAddress, "/hubs/orders"),
                options =>
                {
                    // Routes the connection through the TestServer instead of a socket. Without
                    // these two lines the client tries to open a real WebSocket to a server that
                    // is not listening on any port.
                    options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                    options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;

                    if (accessToken is not null)
                    {
                        options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
                    }
                })
            .Build();

    private async Task<string> GetTokenAsync()
    {
        using HttpClient client = await factory.CreateAuthenticatedClientAsync("Admin");
        return client.DefaultRequestHeaders.Authorization!.Parameter!;
    }
}
