using LogiFlow.Api.Infrastructure;
using LogiFlow.Application.Abstractions.Messaging;
using LogiFlow.Application.Features.Inventory;
using LogiFlow.Application.Features.Products;
using LogiFlow.Application.Features.Reporting;
using LogiFlow.Application.Features.Shipments;
using LogiFlow.Domain.Results;

namespace LogiFlow.Api.Endpoints;

/// <summary>HTTP surface for the product catalogue.</summary>
public static class ProductEndpoints
{
    /// <summary>Registers the <c>products</c> routes, under whichever version group they are mapped onto.</summary>
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app
            .MapGroup("/products")
            .WithTags("Products");

        // Anonymous: the catalogue is public. Explicitly stated rather than left to inherit,
        // so a reader can tell at a glance that it was a decision and not an oversight.
        group.MapGet("/{sku}", async (
                string sku,
                IDispatcher dispatcher,
                CancellationToken cancellationToken) =>
            {
                Result<ProductDto> result = await dispatcher.SendAsync(
                    new GetProductBySkuQuery(sku),
                    cancellationToken);

                return result.ToHttpResult();
            })
            .AllowAnonymous()
            .WithName("GetProductBySku")
            .WithSummary("Looks up a product by SKU.")
            .Produces<ProductDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            // Response caching at the HTTP layer, on top of the query's own Redis cache. The two
            // are complementary: this one stops the request reaching the application at all.
            .CacheOutput(policy => policy.Expire(TimeSpan.FromMinutes(5)).Tag("products"));

        group.MapPost("/", async (
                CreateProductCommand command,
                IDispatcher dispatcher,
                CancellationToken cancellationToken) =>
            {
                Result<Guid> result = await dispatcher.SendAsync(command, cancellationToken);

                return result.ToCreatedResult(id => $"/api/products/{id}");
            })
            // Writing to the catalogue is an administrative act.
            .RequireAuthorization(AuthorizationPolicies.CatalogManager)
            .WithName("CreateProduct")
            .WithSummary("Adds a product to the catalogue.")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPatch("/{productId:guid}/price", async (
                Guid productId,
                ChangePriceRequest request,
                IDispatcher dispatcher,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                Result result = await dispatcher.SendAsync(
                    new ChangeProductPriceCommand(productId, request.NewPrice),
                    cancellationToken);

                return result.ToHttpResult();
            })
            .RequireAuthorization(AuthorizationPolicies.CatalogManager)
            .WithName("ChangeProductPrice")
            // PATCH, not PUT: this modifies one field, it does not replace the product.
            .WithSummary("Changes a product's list price.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();

        return app;
    }
}

/// <summary>HTTP surface for warehouse stock.</summary>
public static class InventoryEndpoints
{
    /// <summary>Registers the <c>inventory</c> routes, under whichever version group they are mapped onto.</summary>
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app
            .MapGroup("/inventory")
            .WithTags("Inventory")
            .RequireAuthorization(AuthorizationPolicies.WarehouseStaff);

        group.MapGet("/warehouses/{warehouseId:guid}/products/{productId:guid}", async (
                Guid warehouseId,
                Guid productId,
                IDispatcher dispatcher,
                CancellationToken cancellationToken) =>
            {
                Result<StockLevelDto> result = await dispatcher.SendAsync(
                    new GetStockLevelQuery(warehouseId, productId),
                    cancellationToken);

                return result.ToHttpResult();
            })
            .WithName("GetStockLevel")
            .WithSummary("Reads live stock levels. Deliberately never cached.")
            .Produces<StockLevelDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/receipts", async (
                ReceiveStockCommand command,
                IDispatcher dispatcher,
                CancellationToken cancellationToken) =>
            {
                Result result = await dispatcher.SendAsync(command, cancellationToken);

                return result.ToHttpResult();
            })
            .WithName("ReceiveStock")
            .WithSummary("Books a supplier delivery into a warehouse.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();

        group.MapPost("/adjustments", async (
                AdjustStockCommand command,
                IDispatcher dispatcher,
                CancellationToken cancellationToken) =>
            {
                Result result = await dispatcher.SendAsync(command, cancellationToken);

                return result.ToHttpResult();
            })
            .WithName("AdjustStock")
            .WithSummary("Applies a physical stock take. Requires a reason; always audited.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();

        return app;
    }
}

/// <summary>HTTP surface for shipments, including the carrier webhook.</summary>
public static class ShipmentEndpoints
{
    /// <summary>Registers the <c>shipments</c> routes, under whichever version group they are mapped onto.</summary>
    public static IEndpointRouteBuilder MapShipmentEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app
            .MapGroup("/shipments")
            .WithTags("Shipments")
            .RequireAuthorization(AuthorizationPolicies.WarehouseStaff);

        group.MapPost("/{shipmentId:guid}/dispatch", async (
                Guid shipmentId,
                DispatchRequest request,
                IDispatcher dispatcher,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                Result result = await dispatcher.SendAsync(
                    new DispatchShipmentCommand(shipmentId, request.TrackingNumber, request.EstimatedDeliveryDate),
                    cancellationToken);

                return result.ToHttpResult();
            })
            .WithName("DispatchShipment")
            .WithSummary("Hands a prepared shipment to its carrier.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();

        // ── Carrier webhook ──────────────────────────────────────────────────────────────
        // Keyed on the tracking number, because that is the only identifier the carrier holds.
        //
        // IDEMPOTENT ON PURPOSE. Carriers retry aggressively and often deliver duplicates. A
        // repeat must return success, not 409 - otherwise the carrier keeps retrying forever
        // and someone gets paged at 3am for a parcel that arrived fine.
        //
        // In production this endpoint would verify a shared secret or HMAC signature rather
        // than a bearer token, since the caller is a machine that has never seen your IdP.
        group.MapPost("/webhooks/delivery", async (
                ConfirmDeliveryCommand command,
                IDispatcher dispatcher,
                CancellationToken cancellationToken) =>
            {
                Result result = await dispatcher.SendAsync(command, cancellationToken);

                return result.ToHttpResult();
            })
            .AllowAnonymous()
            .WithName("ConfirmDelivery")
            .WithSummary("Carrier delivery confirmation. Idempotent: safe to retry.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}

/// <summary>HTTP surface for analytics.</summary>
public static class ReportingEndpoints
{
    /// <summary>Registers the <c>reports</c> routes, under whichever version group they are mapped onto.</summary>
    public static IEndpointRouteBuilder MapReportingEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app
            .MapGroup("/reports")
            .WithTags("Reporting")
            .RequireAuthorization(AuthorizationPolicies.Analyst);

        group.MapGet("/sales", async (
                DateTimeOffset from,
                DateTimeOffset to,
                SalesPeriod period,
                IDispatcher dispatcher,
                CancellationToken cancellationToken) =>
            {
                Result<IReadOnlyList<SalesByPeriodDto>> result = await dispatcher.SendAsync(
                    new GetSalesReportQuery(from, to, period),
                    cancellationToken);

                return result.ToHttpResult();
            })
            .WithName("GetSalesReport")
            .WithSummary("Revenue and volume, bucketed by day, week, month or quarter.")
            .Produces<IReadOnlyList<SalesByPeriodDto>>()
            .ProducesValidationProblem();

        group.MapGet("/top-products", async (
                DateTimeOffset from,
                DateTimeOffset to,
                IDispatcher dispatcher,
                CancellationToken cancellationToken,
                int take = 10) =>
            {
                Result<IReadOnlyList<TopProductDto>> result = await dispatcher.SendAsync(
                    new GetTopProductsQuery(from, to, take),
                    cancellationToken);

                return result.ToHttpResult();
            })
            .WithName("GetTopProducts")
            .WithSummary("Best-selling products in a window.")
            .Produces<IReadOnlyList<TopProductDto>>();

        group.MapGet("/customers", async (
                IDispatcher dispatcher,
                CancellationToken cancellationToken,
                int minimumOrders = 1) =>
            {
                Result<IReadOnlyList<CustomerOrderStatsDto>> result = await dispatcher.SendAsync(
                    new GetCustomerStatsQuery(minimumOrders),
                    cancellationToken);

                return result.ToHttpResult();
            })
            .WithName("GetCustomerStats")
            .WithSummary("Lifetime value and recency per customer.")
            .Produces<IReadOnlyList<CustomerOrderStatsDto>>();

        group.MapGet("/low-stock", async (
                IDispatcher dispatcher,
                CancellationToken cancellationToken) =>
            {
                Result<IReadOnlyList<LowStockDto>> result = await dispatcher.SendAsync(
                    new GetLowStockQuery(),
                    cancellationToken);

                return result.ToHttpResult();
            })
            .WithName("GetLowStock")
            .WithSummary("Products at or below their reorder threshold, worst first.")
            .Produces<IReadOnlyList<LowStockDto>>();

        return app;
    }
}

/// <summary>Request body for a price change.</summary>
/// <param name="NewPrice">The new list price.</param>
public sealed record ChangePriceRequest(decimal NewPrice);

/// <summary>Request body for dispatching a shipment.</summary>
/// <param name="TrackingNumber">Reference issued by the carrier.</param>
/// <param name="EstimatedDeliveryDate">Carrier's estimate, if given.</param>
public sealed record DispatchRequest(string TrackingNumber, DateOnly? EstimatedDeliveryDate);
