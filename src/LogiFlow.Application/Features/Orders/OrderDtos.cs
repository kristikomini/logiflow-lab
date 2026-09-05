using LogiFlow.Domain.Orders;

namespace LogiFlow.Application.Features.Orders;

// ─────────────────────────────────────────────────────────────────────────────────────────
//  READ MODELS
//
//  These are NOT the domain entities. That is the point, and it is worth being explicit about
//  why, because "just return the entity" is the most common shortcut in .NET codebases:
//
//    1. Serialising an aggregate exposes internals. `Order` has a RowVersion, private
//       collections and domain events. None of that belongs in a JSON payload.
//    2. It creates a hidden API contract. Rename a domain property to improve the model and you
//       have silently broken every client. A DTO decouples the two so the domain can evolve.
//    3. It causes lazy-loading disasters. Serialising an entity with navigations either triggers
//       a query per property or throws on a circular reference.
//    4. It over-fetches. A list view needs six columns; loading full aggregates to render it
//       pulls every column of every row plus their children.
//
//  Because these are projected with Select() directly in SQL, the database only ever returns
//  the columns below - typically a fraction of the row width. See OrderQueries in Infrastructure.
//
//  Covered in: course/module-08-cqrs/04-read-models-and-projections.md
// ─────────────────────────────────────────────────────────────────────────────────────────

/// <summary>Compact order representation for lists and search results.</summary>
/// <param name="Id">Order identity.</param>
/// <param name="OrderNumber">Human-facing reference.</param>
/// <param name="CustomerId">Who placed it.</param>
/// <param name="CustomerName">Denormalised for display, so the client needs no second call.</param>
/// <param name="Status">Current lifecycle state.</param>
/// <param name="TotalAmount">What the customer pays.</param>
/// <param name="Currency">ISO code of <paramref name="TotalAmount"/>.</param>
/// <param name="LineCount">How many distinct products.</param>
/// <param name="CreatedAtUtc">When the draft was opened.</param>
/// <param name="SubmittedAtUtc">When it was placed, if it has been.</param>
public sealed record OrderSummaryDto(
    Guid Id,
    string OrderNumber,
    Guid CustomerId,
    string CustomerName,
    OrderStatus Status,
    decimal TotalAmount,
    string Currency,
    int LineCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? SubmittedAtUtc);

/// <summary>Full order representation for a detail view.</summary>
/// <param name="Id">Order identity.</param>
/// <param name="OrderNumber">Human-facing reference.</param>
/// <param name="CustomerId">Who placed it.</param>
/// <param name="CustomerName">Trading name.</param>
/// <param name="CustomerEmail">Contact address.</param>
/// <param name="Status">Current lifecycle state.</param>
/// <param name="Currency">ISO code for every amount below.</param>
/// <param name="Subtotal">Sum of line totals.</param>
/// <param name="DiscountAmount">Tier discount applied.</param>
/// <param name="ShippingCost">Delivery charge.</param>
/// <param name="Total">What the customer pays.</param>
/// <param name="TotalWeightGrams">Combined shipping weight.</param>
/// <param name="ShippingAddress">Where it goes.</param>
/// <param name="Lines">The products ordered.</param>
/// <param name="CreatedAtUtc">When the draft was opened.</param>
/// <param name="SubmittedAtUtc">When it was placed.</param>
/// <param name="ShippedAtUtc">When it left the warehouse.</param>
/// <param name="DeliveredAtUtc">When it arrived.</param>
/// <param name="CancellationReason">Why it was cancelled, if it was.</param>
/// <param name="AllowedTransitions">
/// Which actions the client may offer right now, straight from <see cref="OrderStateMachine"/>.
/// Sending this means the UI never has to reimplement the state machine in TypeScript and get
/// it subtly wrong — a genuinely underused API design technique.
/// </param>
public sealed record OrderDetailDto(
    Guid Id,
    string OrderNumber,
    Guid CustomerId,
    string CustomerName,
    string CustomerEmail,
    OrderStatus Status,
    string Currency,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal ShippingCost,
    decimal Total,
    int TotalWeightGrams,
    AddressDto? ShippingAddress,
    IReadOnlyList<OrderLineDto> Lines,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? SubmittedAtUtc,
    DateTimeOffset? ShippedAtUtc,
    DateTimeOffset? DeliveredAtUtc,
    string? CancellationReason,
    IReadOnlyList<OrderStatus> AllowedTransitions);

/// <summary>One product line, as returned to a client.</summary>
/// <param name="Id">Line identity.</param>
/// <param name="ProductId">The product.</param>
/// <param name="Sku">Product code at order time.</param>
/// <param name="ProductName">Product name at order time.</param>
/// <param name="Quantity">Units ordered.</param>
/// <param name="UnitPrice">Price per unit at order time.</param>
/// <param name="LineTotal">Quantity times unit price.</param>
public sealed record OrderLineDto(
    Guid Id,
    Guid ProductId,
    string Sku,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);

/// <summary>A postal address, flattened for transport.</summary>
/// <param name="Line1">Street and number.</param>
/// <param name="Line2">Apartment or suite.</param>
/// <param name="City">City or town.</param>
/// <param name="Region">State or province.</param>
/// <param name="PostalCode">Postal or ZIP code.</param>
/// <param name="CountryCode">ISO 3166-1 alpha-2 code.</param>
public sealed record AddressDto(
    string Line1,
    string? Line2,
    string City,
    string? Region,
    string PostalCode,
    string CountryCode);
