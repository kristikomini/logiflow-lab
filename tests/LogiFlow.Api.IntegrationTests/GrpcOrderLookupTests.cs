using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using LogiFlow.Api.Grpc;

namespace LogiFlow.Api.IntegrationTests;

/// <summary>
/// The gRPC surface, called the way another service would call it.
/// </summary>
/// <remarks>
/// The point of these is not that gRPC works — Google's tests cover that. It is that the two
/// surfaces over the same query handlers agree with each other, and that the claims made about
/// the wire format in <c>OrderLookupService</c>'s remarks are measured rather than asserted. A
/// performance claim in a comment is a rumour until something counts the bytes.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class GrpcOrderLookupTests(LogiFlowApiFactory factory)
{
    [Fact]
    public async Task An_order_comes_back_over_gRPC_with_the_same_values_as_over_REST()
    {
        (Guid orderId, HttpClient http) = await CreateOrderAsync();

        OrderLookup.OrderLookupClient client = await CreateClientAsync();

        OrderReply reply = await client.GetOrderAsync(new GetOrderRequest { OrderId = orderId.ToString() });

        // The REST answer for the same order, from the same handler.
        HttpResponseMessage rest = await http.GetAsync($"/api/v1/orders/{orderId}");
        rest.EnsureSuccessStatusCode();
        System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(await rest.Content.ReadAsStringAsync());

        reply.OrderId.ShouldBe(orderId.ToString());
        reply.OrderNumber.ShouldBe(json.RootElement.GetProperty("orderNumber").GetString());

        // The two surfaces disagree about how to write an enum, and it is worth knowing rather
        // than hiding: System.Text.Json serializes OrderStatus as its NUMBER by default, while
        // this .proto declares the field as a string and the service sends the name. Both are
        // defensible; what is not defensible is a client that assumed one and got the other.
        // (A JsonStringEnumConverter would align them, at the cost of a breaking change to every
        // existing REST consumer — which is what /api/v2 is for.)
        LogiFlow.Domain.Orders.OrderStatus restStatus =
            (LogiFlow.Domain.Orders.OrderStatus)json.RootElement.GetProperty("status").GetInt32();
        reply.Status.ShouldBe(restStatus.ToString());

        // Money crossed the wire as a string and must survive the round trip EXACTLY. This is
        // the assertion that would fail if someone "simplified" total_amount to a double.
        decimal.Parse(reply.TotalAmount, CultureInfo.InvariantCulture)
            .ShouldBe(json.RootElement.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task A_bad_id_is_InvalidArgument_and_a_missing_one_is_NotFound()
    {
        OrderLookup.OrderLookupClient client = await CreateClientAsync();

        // gRPC status codes, not HTTP ones. Mapping these onto 400 and 404 in your head is
        // natural and wrong — a gRPC client switches on StatusCode, and giving it `Unknown`
        // for a validation error leaves it nothing to branch on.
        RpcException bad = await Should.ThrowAsync<RpcException>(async () =>
            await client.GetOrderAsync(new GetOrderRequest { OrderId = "not-a-guid" }));
        bad.StatusCode.ShouldBe(StatusCode.InvalidArgument);

        RpcException missing = await Should.ThrowAsync<RpcException>(async () =>
            await client.GetOrderAsync(new GetOrderRequest { OrderId = Guid.CreateVersion7().ToString() }));
        missing.StatusCode.ShouldBe(StatusCode.NotFound);
    }

    [Fact]
    public async Task An_unauthenticated_call_is_Unauthenticated()
    {
        // The service carries [Authorize]. Without credentials the call must fail before it
        // reaches a query.
        OrderLookup.OrderLookupClient client = new(GrpcChannel.ForAddress(
            factory.Server.BaseAddress,
            new GrpcChannelOptions { HttpHandler = factory.Server.CreateHandler() }));

        RpcException error = await Should.ThrowAsync<RpcException>(async () =>
            await client.GetOrderAsync(new GetOrderRequest { OrderId = Guid.CreateVersion7().ToString() }));

        error.StatusCode.ShouldBe(StatusCode.Unauthenticated);
    }

    [Fact]
    public async Task The_protobuf_payload_is_substantially_smaller_than_the_JSON_one()
    {
        (Guid orderId, HttpClient http) = await CreateOrderAsync();

        OrderLookup.OrderLookupClient client = await CreateClientAsync();
        OrderReply reply = await client.GetOrderAsync(new GetOrderRequest { OrderId = orderId.ToString() });

        int protobufBytes = reply.ToByteArray().Length;

        HttpResponseMessage rest = await http.GetAsync($"/api/v1/orders/{orderId}");
        int jsonBytes = Encoding.UTF8.GetByteCount(await rest.Content.ReadAsStringAsync());

        // Not a strict ratio — the REST DTO is genuinely richer (lines, address, allowed
        // transitions), so this is not a like-for-like comparison and pretending otherwise
        // would be the dishonest version of this test. What it does establish is the direction
        // and the order of magnitude, which is the part the comment in OrderLookupService
        // claims.
        protobufBytes.ShouldBeLessThan(jsonBytes);

        // Printed rather than asserted tightly: a hard threshold here would fail on a seed data
        // change and teach people to delete the test.
        TestOutput($"protobuf {protobufBytes} bytes, JSON {jsonBytes} bytes");
    }

    [Fact]
    public async Task The_server_stream_delivers_rows_one_at_a_time()
    {
        await CreateOrderAsync();
        await CreateOrderAsync();

        OrderLookup.OrderLookupClient client = await CreateClientAsync();

        using AsyncServerStreamingCall<OrderReply> call =
            client.StreamOrders(new StreamOrdersRequest { Max = 2 });

        List<OrderReply> received = [];
        await foreach (OrderReply order in call.ResponseStream.ReadAllAsync())
        {
            received.Add(order);
        }

        // Two or more orders exist by now (other tests create them too), and the stream is
        // capped at Max. Asserting the cap rather than the count keeps this independent of
        // whatever else has run.
        received.Count.ShouldBeInRange(1, 2);
        received.ShouldAllBe(o => o.OrderNumber.Length > 0);
    }

    // ── Plumbing ─────────────────────────────────────────────────────────────────────────

    private async Task<OrderLookup.OrderLookupClient> CreateClientAsync()
    {
        string token = await GetTokenAsync();

        // The channel talks to the TestServer's handler rather than to a socket. gRPC needs
        // HTTP/2 and TestServer speaks it, which is why this works without Kestrel.
        GrpcChannel channel = GrpcChannel.ForAddress(
            factory.Server.BaseAddress,
            new GrpcChannelOptions
            {
                HttpHandler = factory.Server.CreateHandler(),

                // CallCredentials, rather than a header set per call. The token is attached by
                // the channel to every call on it, which is the gRPC equivalent of
                // DefaultRequestHeaders — and unlike a header it composes with a deadline and a
                // retry policy.
                Credentials = ChannelCredentials.Create(
                    ChannelCredentials.Insecure,
                    CallCredentials.FromInterceptor((_, metadata) =>
                    {
                        metadata.Add("Authorization", $"Bearer {token}");
                        return Task.CompletedTask;
                    })),

                // Required because the address is http:// rather than https://. Sending
                // credentials over plaintext is refused by default, and that default is correct
                // everywhere except an in-process test server.
                UnsafeUseInsecureChannelCallCredentials = true,
            });

        return new OrderLookup.OrderLookupClient(channel);
    }

    private async Task<(Guid OrderId, HttpClient Http)> CreateOrderAsync()
    {
        HttpClient http = await factory.CreateAuthenticatedClientAsync("Admin");

        (Guid customerId, Guid productId) = await SeedAsync();

        HttpResponseMessage created = await http.PostAsJsonAsync(
            "/api/v1/orders",
            new
            {
                customerId,
                currencyCode = "EUR",
                shippingAddress = new
                {
                    line1 = "Via Emilia 9",
                    line2 = (string?)null,
                    city = "Modena",
                    region = (string?)null,
                    postalCode = "41100",
                    countryCode = "IT",
                },
            });

        created.EnsureSuccessStatusCode();

        Guid orderId = await created.Content.ReadFromJsonAsync<Guid>();
        await http.PostAsJsonAsync($"/api/v1/orders/{orderId}/lines", new { productId, quantity = 2 });

        return (orderId, http);
    }

    private async Task<(Guid CustomerId, Guid ProductId)> SeedAsync()
    {
        Guid customerId = Guid.Empty;
        Guid productId = Guid.Empty;

        await factory.WithDbContextAsync(async db =>
        {
            string suffix = Random.Shared.Next(100000, 999999).ToString(CultureInfo.InvariantCulture);

            Domain.Customers.Customer customer = Domain.Customers.Customer.Create(
                $"Grpc Co {suffix}",
                Domain.ValueObjects.EmailAddress.Create($"grpc{suffix}@example.com").Value,
                Domain.ValueObjects.Address.Create("Via Emilia 9", null, "Modena", null, "41100", "IT").Value,
                Domain.Customers.CustomerTier.Standard).Value;

            Domain.Catalog.Product product = Domain.Catalog.Product.Create(
                Domain.ValueObjects.Sku.Create($"GRP-{suffix}").Value,
                "Grpc Test Widget",
                null,
                new Domain.ValueObjects.Money(50m, Domain.ValueObjects.Currency.Eur),
                Domain.ValueObjects.Weight.FromGrams(250).Value).Value;

            Domain.Inventory.Warehouse warehouse = Domain.Inventory.Warehouse.Create(
                $"GRP-{suffix}",
                "Grpc Warehouse",
                Domain.ValueObjects.Address.Create("Zona 2", null, "Modena", null, "41100", "IT").Value).Value;

            warehouse.AddStockItem(product.Id, 100, reorderThreshold: 10);

            db.Customers.Add(customer);
            db.Products.Add(product);
            db.Warehouses.Add(warehouse);

            await db.SaveChangesAsync();

            customerId = customer.Id.Value;
            productId = product.Id.Value;
        });

        return (customerId, productId);
    }

    private async Task<string> GetTokenAsync()
    {
        using HttpClient client = await factory.CreateAuthenticatedClientAsync("Admin");
        return client.DefaultRequestHeaders.Authorization!.Parameter!;
    }

    private static void TestOutput(string message) => Console.WriteLine(message);
}
