using System.Net;
using System.Net.Http.Json;
using LogiFlow.Domain.Catalog;
using LogiFlow.Domain.Customers;
using LogiFlow.Domain.Inventory;
using LogiFlow.Domain.Orders;
using LogiFlow.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace LogiFlow.Api.IntegrationTests;

/// <summary>
/// End-to-end tests: real HTTP, real pipeline, real SQL Server.
/// </summary>
/// <remarks>
/// These are the tests that catch what unit tests cannot — mapping mistakes, missing migrations,
/// broken JSON contracts, middleware ordering bugs, and domain events that never fire because
/// an interceptor was not registered. Slower and far fewer than unit tests, and they are the
/// ones that let you deploy on a Friday.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class OrderLifecycleTests(LogiFlowApiFactory factory)
{
    /// <summary>Seeds a customer, a product and a stocked warehouse, and returns their ids.</summary>
    private async Task<(Guid CustomerId, Guid ProductId)> SeedAsync(
        CustomerTier tier = CustomerTier.Standard,
        int stock = 100)
    {
        Guid customerId = Guid.Empty;
        Guid productId = Guid.Empty;

        await factory.WithDbContextAsync(async db =>
        {
            // A unique SKU and email per call, so tests sharing the container cannot collide on
            // the unique indexes. Deterministic-per-test data beats a shared fixture.
            string suffix = Random.Shared.Next(100000, 999999).ToString(System.Globalization.CultureInfo.InvariantCulture);

            Customer customer = Customer.Create(
                $"Test Co {suffix}",
                EmailAddress.Create($"test{suffix}@example.com").Value,
                Address.Create("Via Roma 1", null, "Milano", null, "20100", "IT").Value,
                tier).Value;

            Product product = Product.Create(
                Sku.Create($"ELE-{suffix}").Value,
                "Integration Test Widget",
                null,
                new Money(100m, Currency.Eur),
                Weight.FromGrams(500).Value).Value;

            Warehouse warehouse = Warehouse.Create(
                $"TST-{suffix}",
                "Test Warehouse",
                Address.Create("Zona 1", null, "Milano", null, "20090", "IT").Value).Value;

            warehouse.AddStockItem(product.Id, stock, reorderThreshold: 10);

            db.Customers.Add(customer);
            db.Products.Add(product);
            db.Warehouses.Add(warehouse);

            await db.SaveChangesAsync();

            customerId = customer.Id.Value;
            productId = product.Id.Value;
        });

        return (customerId, productId);
    }

    private static object AnAddress() => new
    {
        line1 = "Via Roma 10",
        line2 = (string?)null,
        city = "Milano",
        region = (string?)null,
        postalCode = "20100",
        countryCode = "IT",
    };

    [Fact]
    public async Task Full_order_lifecycle_works_over_HTTP()
    {
        (Guid customerId, Guid productId) = await SeedAsync();
        HttpClient client = await factory.CreateAuthenticatedClientAsync();

        // ── Create ───────────────────────────────────────────────────────────────────────
        HttpResponseMessage created = await client.PostAsJsonAsync(
            "/api/orders",
            new { customerId, currencyCode = "EUR", shippingAddress = AnAddress() });

        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        // 201 must carry a Location header pointing at the new resource.
        created.Headers.Location.ShouldNotBeNull();

        Guid orderId = await created.Content.ReadFromJsonAsync<Guid>();
        orderId.ShouldNotBe(Guid.Empty);

        // ── Add lines ────────────────────────────────────────────────────────────────────
        (await client.PostAsJsonAsync($"/api/orders/{orderId}/lines", new { productId, quantity = 3 }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Same product again: must merge, not duplicate.
        (await client.PostAsJsonAsync($"/api/orders/{orderId}/lines", new { productId, quantity = 2 }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // ── Read back ────────────────────────────────────────────────────────────────────
        OrderDetailResponse? detail = await client.GetFromJsonAsync<OrderDetailResponse>($"/api/orders/{orderId}");

        detail.ShouldNotBeNull();
        detail.Lines.Count.ShouldBe(1);
        detail.Lines[0].Quantity.ShouldBe(5);
        detail.Subtotal.ShouldBe(500m);
        detail.Status.ShouldBe((int)OrderStatus.Draft);
        detail.OrderNumber.ShouldStartWith("ORD-");

        // ── Submit ───────────────────────────────────────────────────────────────────────
        (await client.PostAsync($"/api/orders/{orderId}/submit", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // ── Assert against the DATABASE, not the API ─────────────────────────────────────
        // Asking the API whether the API worked is circular. Reading the row proves the domain
        // event fired, the interceptor ran, and the write committed.
        await factory.WithDbContextAsync(async db =>
        {
            var typedId = OrderId.From(orderId);

            Order? order = await db.Orders.FirstOrDefaultAsync(o => o.Id == typedId);
            order.ShouldNotBeNull();
            order.Status.ShouldBe(OrderStatus.Submitted);

            // Set by ReserveStockOnOrderSubmitted, inside the same transaction.
            order.FulfillingWarehouseId.ShouldNotBeNull();

            // The stock was actually reserved.
            var typedProductId = ProductId.From(productId);
            StockItem? stock = await db.StockItems
                .FirstOrDefaultAsync(s => s.ProductId == typedProductId);

            stock.ShouldNotBeNull();
            stock.QuantityReserved.ShouldBe(5);

            // And the outbox row was written in the same transaction.
            bool hasOutbox = await db.OutboxMessages
                .AnyAsync(m => m.Type.Contains("OrderSubmittedDomainEvent"));

            hasOutbox.ShouldBeTrue();
        });
    }

    [Fact]
    public async Task Cancelling_a_submitted_order_releases_the_reserved_stock()
    {
        (Guid customerId, Guid productId) = await SeedAsync(stock: 50);
        HttpClient client = await factory.CreateAuthenticatedClientAsync();

        HttpResponseMessage created = await client.PostAsJsonAsync(
            "/api/orders",
            new { customerId, currencyCode = "EUR", shippingAddress = AnAddress() });

        Guid orderId = await created.Content.ReadFromJsonAsync<Guid>();

        await client.PostAsJsonAsync($"/api/orders/{orderId}/lines", new { productId, quantity = 4 });
        await client.PostAsync($"/api/orders/{orderId}/submit", null);

        HttpResponseMessage cancelled = await client.PostAsJsonAsync(
            $"/api/orders/{orderId}/cancel",
            new { reason = "Customer changed their mind" });

        cancelled.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await factory.WithDbContextAsync(async db =>
        {
            var typedProductId = ProductId.From(productId);

            StockItem? stock = await db.StockItems.FirstOrDefaultAsync(s => s.ProductId == typedProductId);

            stock.ShouldNotBeNull();

            // Back to zero: the release handler ran and gave the units back to the right site.
            stock.QuantityReserved.ShouldBe(0);
            stock.QuantityOnHand.ShouldBe(50);
        });
    }

    [Fact]
    public async Task Editing_a_submitted_order_returns_409_with_a_problem_document()
    {
        (Guid customerId, Guid productId) = await SeedAsync();
        HttpClient client = await factory.CreateAuthenticatedClientAsync();

        HttpResponseMessage created = await client.PostAsJsonAsync(
            "/api/orders",
            new { customerId, currencyCode = "EUR", shippingAddress = AnAddress() });

        Guid orderId = await created.Content.ReadFromJsonAsync<Guid>();
        await client.PostAsJsonAsync($"/api/orders/{orderId}/lines", new { productId, quantity = 1 });
        await client.PostAsync($"/api/orders/{orderId}/submit", null);

        HttpResponseMessage conflict = await client.PostAsJsonAsync(
            $"/api/orders/{orderId}/lines",
            new { productId, quantity = 1 });

        conflict.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        ProblemResponse? problem = await conflict.Content.ReadFromJsonAsync<ProblemResponse>();
        problem.ShouldNotBeNull();

        // The stable machine-readable code, which clients branch on.
        problem.Code.ShouldBe("Order.NotEditable");
    }

    [Fact]
    public async Task Insufficient_stock_rolls_the_whole_submission_back()
    {
        // The transactional guarantee, tested rather than assumed: the reservation handler
        // throws, SaveChanges aborts, and the order must NOT be left in Submitted.
        (Guid customerId, Guid productId) = await SeedAsync(stock: 2);
        HttpClient client = await factory.CreateAuthenticatedClientAsync();

        HttpResponseMessage created = await client.PostAsJsonAsync(
            "/api/orders",
            new { customerId, currencyCode = "EUR", shippingAddress = AnAddress() });

        Guid orderId = await created.Content.ReadFromJsonAsync<Guid>();

        await client.PostAsJsonAsync($"/api/orders/{orderId}/lines", new { productId, quantity = 10 });

        HttpResponseMessage submit = await client.PostAsync($"/api/orders/{orderId}/submit", null);

        submit.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await factory.WithDbContextAsync(async db =>
        {
            var typedId = OrderId.From(orderId);

            Order? order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == typedId);
            order.ShouldNotBeNull();

            // Still a draft. If this ever reads Submitted, the transaction boundary is broken
            // and you have orders promising stock that does not exist.
            order.Status.ShouldBe(OrderStatus.Draft);
        });
    }

    [Fact]
    public async Task Protected_endpoints_reject_anonymous_callers()
    {
        HttpClient anonymous = factory.CreateClient();

        HttpResponseMessage response = await anonymous.GetAsync("/api/orders");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Role_policies_are_enforced()
    {
        // Authenticated, but only as an Analyst - which the CatalogManager policy excludes.
        HttpClient analyst = await factory.CreateAuthenticatedClientAsync("Analyst");

        HttpResponseMessage response = await analyst.PostAsJsonAsync(
            "/api/products",
            new
            {
                sku = "ELE-999999",
                name = "Should not be created",
                description = (string?)null,
                unitPrice = 10m,
                currencyCode = "EUR",
                weightGrams = 100,
            });

        // 403, not 401. The caller IS authenticated; they are simply not allowed.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Validation_failures_return_400_with_per_field_detail()
    {
        (Guid customerId, Guid productId) = await SeedAsync();
        HttpClient client = await factory.CreateAuthenticatedClientAsync();

        HttpResponseMessage created = await client.PostAsJsonAsync(
            "/api/orders",
            new { customerId, currencyCode = "EUR", shippingAddress = AnAddress() });

        Guid orderId = await created.Content.ReadFromJsonAsync<Guid>();

        HttpResponseMessage bad = await client.PostAsJsonAsync(
            $"/api/orders/{orderId}/lines",
            new { productId, quantity = 0 });

        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        ValidationProblemResponse? problem =
            await bad.Content.ReadFromJsonAsync<ValidationProblemResponse>();

        problem.ShouldNotBeNull();
        problem.Errors.ShouldContainKey("Quantity");
    }

    [Fact]
    public async Task Page_size_is_clamped_server_side()
    {
        HttpClient client = await factory.CreateAuthenticatedClientAsync();

        // The DoS guard: asking for a million rows must not return a million rows.
        PagedResponse? page = await client.GetFromJsonAsync<PagedResponse>(
            "/api/orders?page=1&pageSize=1000000");

        page.ShouldNotBeNull();
        page.PageSize.ShouldBe(100);   // MaxPageSize
    }

    // ── Response shapes ─────────────────────────────────────────────────────────────────
    // Deliberately declared here rather than reusing the Application layer's DTOs. A test that
    // shares the production type cannot detect a breaking change to the wire contract - rename
    // a property and both sides move together, silently breaking every real client.

    private sealed record OrderDetailResponse(
        Guid Id,
        string OrderNumber,
        int Status,
        decimal Subtotal,
        decimal Total,
        IReadOnlyList<OrderLineResponse> Lines);

    private sealed record OrderLineResponse(Guid Id, string Sku, int Quantity, decimal UnitPrice);

    private sealed record ProblemResponse(string Title, int Status, string Detail, string Code);

    private sealed record ValidationProblemResponse(
        string Title,
        int Status,
        Dictionary<string, string[]> Errors);

    private sealed record PagedResponse(int Page, int PageSize, int TotalCount);
}
