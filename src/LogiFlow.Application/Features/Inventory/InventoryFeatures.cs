using FluentValidation;
using LogiFlow.Application.Abstractions.Data;
using LogiFlow.Application.Abstractions.Messaging;
using LogiFlow.Domain.Catalog;
using LogiFlow.Domain.Inventory;
using LogiFlow.Domain.Results;
using Microsoft.Extensions.Logging;

namespace LogiFlow.Application.Features.Inventory;

/// <summary>Books a supplier delivery into a warehouse.</summary>
/// <param name="WarehouseId">Receiving site.</param>
/// <param name="ProductId">What arrived.</param>
/// <param name="Quantity">How many units.</param>
public sealed record ReceiveStockCommand(Guid WarehouseId, Guid ProductId, int Quantity) : ICommand;

/// <summary>Input validation for <see cref="ReceiveStockCommand"/>.</summary>
public sealed class ReceiveStockCommandValidator : AbstractValidator<ReceiveStockCommand>
{
    /// <summary>Configures the rules.</summary>
    public ReceiveStockCommandValidator()
    {
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0).LessThanOrEqualTo(1_000_000);
    }
}

/// <summary>Handles <see cref="ReceiveStockCommand"/>.</summary>
/// <param name="warehouses">Warehouse persistence.</param>
internal sealed class ReceiveStockCommandHandler(IWarehouseRepository warehouses)
    : ICommandHandler<ReceiveStockCommand>
{
    /// <inheritdoc />
    public async Task<Result> HandleAsync(ReceiveStockCommand request, CancellationToken cancellationToken)
    {
        var warehouseId = WarehouseId.From(request.WarehouseId);
        var productId = ProductId.From(request.ProductId);

        Warehouse? warehouse = await warehouses
            .GetWithStockForAsync(warehouseId, [productId], cancellationToken)
            .ConfigureAwait(false);

        if (warehouse is null)
        {
            return InventoryErrors.WarehouseNotFound(warehouseId);
        }

        return warehouse.Receive(productId, request.Quantity);
    }
}

/// <summary>Applies the result of a physical stock take.</summary>
/// <param name="WarehouseId">The site counted.</param>
/// <param name="ProductId">The product counted.</param>
/// <param name="ActualQuantity">What was actually on the shelf.</param>
/// <param name="Reason">Why the recorded figure was wrong.</param>
public sealed record AdjustStockCommand(
    Guid WarehouseId,
    Guid ProductId,
    int ActualQuantity,
    string Reason) : ICommand;

/// <summary>Input validation for <see cref="AdjustStockCommand"/>.</summary>
public sealed class AdjustStockCommandValidator : AbstractValidator<AdjustStockCommand>
{
    /// <summary>Configures the rules.</summary>
    public AdjustStockCommandValidator()
    {
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.ActualQuantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(3).MaximumLength(500);
    }
}

/// <summary>Handles <see cref="AdjustStockCommand"/>.</summary>
/// <param name="warehouses">Warehouse persistence.</param>
/// <param name="logger">Logger.</param>
internal sealed class AdjustStockCommandHandler(
    IWarehouseRepository warehouses,
    ILogger<AdjustStockCommandHandler> logger) : ICommandHandler<AdjustStockCommand>
{
    /// <inheritdoc />
    public async Task<Result> HandleAsync(AdjustStockCommand request, CancellationToken cancellationToken)
    {
        var warehouseId = WarehouseId.From(request.WarehouseId);
        var productId = ProductId.From(request.ProductId);

        Warehouse? warehouse = await warehouses
            .GetWithStockForAsync(warehouseId, [productId], cancellationToken)
            .ConfigureAwait(false);

        if (warehouse is null)
        {
            return InventoryErrors.WarehouseNotFound(warehouseId);
        }

        Result result = warehouse.AdjustStock(productId, request.ActualQuantity, request.Reason);

        if (result.IsSuccess)
        {
            // Stock adjustments are financially material and a common vector for internal theft.
            // Log every one at Information with the actor, so the audit trail exists even if the
            // StockAdjustedDomainEvent handler is later removed.
            logger.LogInformation(
                "Stock adjusted at {WarehouseId} for product {ProductId} to {Quantity}: {Reason}",
                warehouseId,
                productId,
                request.ActualQuantity,
                request.Reason);
        }

        return result;
    }
}

/// <summary>Current stock levels for one product at one site.</summary>
/// <param name="WarehouseId">The site.</param>
/// <param name="WarehouseCode">Site code.</param>
/// <param name="ProductId">The product.</param>
/// <param name="QuantityOnHand">Units physically present.</param>
/// <param name="QuantityReserved">Units promised to orders.</param>
/// <param name="QuantityAvailable">Units still sellable.</param>
/// <param name="ReorderThreshold">Replenishment trigger level.</param>
/// <param name="NeedsReplenishment">Whether the threshold has been reached.</param>
public sealed record StockLevelDto(
    Guid WarehouseId,
    string WarehouseCode,
    Guid ProductId,
    int QuantityOnHand,
    int QuantityReserved,
    int QuantityAvailable,
    int ReorderThreshold,
    bool NeedsReplenishment);

/// <summary>Reads stock levels for a product at a site.</summary>
/// <param name="WarehouseId">The site.</param>
/// <param name="ProductId">The product.</param>
public sealed record GetStockLevelQuery(Guid WarehouseId, Guid ProductId) : IQuery<StockLevelDto>;

/// <summary>Handles <see cref="GetStockLevelQuery"/>.</summary>
/// <remarks>
/// Deliberately not cached. Stock is the single most volatile number in the system and the one
/// where a stale read has the worst consequence — showing "3 in stock" for something sold out
/// two seconds ago produces an order that cannot be fulfilled. Some data must always be live.
/// </remarks>
/// <param name="warehouses">Warehouse lookup.</param>
internal sealed class GetStockLevelQueryHandler(IWarehouseRepository warehouses)
    : IQueryHandler<GetStockLevelQuery, StockLevelDto>
{
    /// <inheritdoc />
    public async Task<Result<StockLevelDto>> HandleAsync(
        GetStockLevelQuery request,
        CancellationToken cancellationToken)
    {
        var warehouseId = WarehouseId.From(request.WarehouseId);
        var productId = ProductId.From(request.ProductId);

        Warehouse? warehouse = await warehouses
            .GetWithStockForAsync(warehouseId, [productId], cancellationToken)
            .ConfigureAwait(false);

        if (warehouse is null)
        {
            return InventoryErrors.WarehouseNotFound(warehouseId);
        }

        StockItem? item = warehouse.Stock.FirstOrDefault(s => s.ProductId == productId);
        if (item is null)
        {
            return InventoryErrors.StockItemNotFound(productId, warehouse.Code);
        }

        return new StockLevelDto(
            warehouse.Id.Value,
            warehouse.Code,
            productId.Value,
            item.QuantityOnHand,
            item.QuantityReserved,
            item.QuantityAvailable,
            item.ReorderThreshold,
            item.NeedsReplenishment);
    }
}
