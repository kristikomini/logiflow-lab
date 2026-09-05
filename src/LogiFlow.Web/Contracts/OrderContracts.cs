namespace LogiFlow.Web.Contracts;

// ─────────────────────────────────────────────────────────────────────────────────────────
//  THE API CONTRACT, AS SEEN FROM OUTSIDE
//
//  These records deliberately duplicate the shapes in LogiFlow.Application.Features.Orders.
//  That looks like a violation of DRY, and it is a decision rather than an oversight.
//
//  Sharing the types would be one project reference and zero duplication. It would also mean:
//
//    * The UI compiles against internal types it has no business knowing. A DTO is only a
//      contract because both ends agreed on it - if one end simply *is* the other end's class,
//      there is no contract, only coupling.
//    * A rename in the Application layer silently becomes a wire-format break. With separate
//      types the deserialiser tells you, which is the whole point.
//    * A non-.NET client (the mobile app, the customer's integration) has to hand-roll these
//      anyway. Writing them here keeps us honest about what the API actually promises.
//
//  The rule of thumb: share types across a process boundary you own on both sides and deploy
//  together; duplicate them across a boundary that is a published contract. This one is a
//  published contract - the API has a Scalar reference page and a version.
//
//  Covered in: course/module-16-the-layer-map/README.md (Entity vs DTO)
// ─────────────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Where an order sits in its lifecycle.
/// </summary>
/// <remarks>
/// <b>The numeric values are not decoration - they are the wire format.</b> The API does not
/// register a <c>JsonStringEnumConverter</c>, so <c>System.Text.Json</c> serialises this enum as
/// an integer. Declaring the values explicitly here means a reordering on either side produces a
/// compile-time-visible mismatch rather than orders that quietly display as "Draft" when they
/// have shipped. Verified against <c>LogiFlow.Domain.Orders.OrderStatus</c>.
/// </remarks>
public enum OrderStatus
{
    /// <summary>Being assembled. Lines can still be added and removed.</summary>
    Draft = 1,

    /// <summary>Placed, awaiting stock reservation and payment authorisation.</summary>
    Submitted = 2,

    /// <summary>Stock reserved and payment authorised.</summary>
    Confirmed = 3,

    /// <summary>Handed to a carrier.</summary>
    Shipped = 4,

    /// <summary>Delivery confirmed. Terminal.</summary>
    Delivered = 5,

    /// <summary>Cancelled before shipping. Terminal.</summary>
    Cancelled = 6,
}

/// <summary>Which column a search is sorted on. Serialised as an integer, as above.</summary>
public enum OrderSortField
{
    /// <summary>By creation timestamp.</summary>
    CreatedAt = 0,

    /// <summary>By submission timestamp.</summary>
    SubmittedAt = 1,

    /// <summary>By order value.</summary>
    Total = 2,

    /// <summary>By order reference.</summary>
    OrderNumber = 3,

    /// <summary>By lifecycle state.</summary>
    Status = 4,
}

/// <summary>Sort direction.</summary>
public enum SortDirection
{
    /// <summary>Smallest first.</summary>
    Ascending = 0,

    /// <summary>Largest first.</summary>
    Descending = 1,
}

/// <summary>One page of results, plus what a pager needs to render itself.</summary>
/// <typeparam name="T">The item type.</typeparam>
/// <param name="Items">The rows on this page.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Rows per page.</param>
/// <param name="TotalCount">Rows matching the filter across all pages.</param>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    /// <summary>Total number of pages.</summary>
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    /// <summary>True when a previous page exists.</summary>
    public bool HasPrevious => Page > 1;

    /// <summary>True when a further page exists.</summary>
    public bool HasNext => Page < TotalPages;
}

/// <summary>Compact order representation for the list view.</summary>
/// <param name="Id">Order identity.</param>
/// <param name="OrderNumber">Human-facing reference.</param>
/// <param name="CustomerId">Who placed it.</param>
/// <param name="CustomerName">Denormalised for display.</param>
/// <param name="Status">Current lifecycle state.</param>
/// <param name="TotalAmount">What the customer pays.</param>
/// <param name="Currency">ISO code.</param>
/// <param name="LineCount">Distinct products.</param>
/// <param name="CreatedAtUtc">When the draft was opened.</param>
/// <param name="SubmittedAtUtc">When it was placed, if it has been.</param>
public sealed record OrderSummary(
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

/// <summary>One product line.</summary>
/// <param name="Id">Line identity.</param>
/// <param name="ProductId">The product.</param>
/// <param name="Sku">Product code at order time.</param>
/// <param name="ProductName">Product name at order time.</param>
/// <param name="Quantity">Units ordered.</param>
/// <param name="UnitPrice">Price per unit at order time.</param>
/// <param name="LineTotal">Quantity times unit price.</param>
public sealed record OrderLine(
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
/// <param name="CountryCode">ISO 3166-1 alpha-2.</param>
public sealed record Address(
    string Line1,
    string? Line2,
    string City,
    string? Region,
    string PostalCode,
    string CountryCode);

/// <summary>Full order representation for the detail view.</summary>
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
/// Which actions the UI may offer right now, computed by the server's state machine.
/// <b>This is why the detail page has no <c>if (status == Draft)</c> anywhere in it.</b> The
/// transition rules live in one place, in the domain, and the client renders whatever it is
/// told. Reimplementing that table in the front end is how the two drift apart.
/// </param>
public sealed record OrderDetail(
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
    Address? ShippingAddress,
    IReadOnlyList<OrderLine> Lines,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? SubmittedAtUtc,
    DateTimeOffset? ShippedAtUtc,
    DateTimeOffset? DeliveredAtUtc,
    string? CancellationReason,
    IReadOnlyList<OrderStatus> AllowedTransitions);

/// <summary>The filter, sort and page state of the orders list.</summary>
/// <remarks>
/// Mutable on purpose: it is bound directly to form controls. It is converted to a query string
/// by <see cref="Services.LogiFlowApiClient"/> rather than being serialised, because the API
/// binds these with <c>[AsParameters]</c> from the query string.
/// </remarks>
public sealed class OrderSearchRequest
{
    /// <summary>1-based page number.</summary>
    public int Page { get; set; } = 1;

    /// <summary>Rows per page. The API clamps this to 100 regardless of what is sent.</summary>
    public int PageSize { get; set; } = 25;

    /// <summary>Free-text match against order number and customer name.</summary>
    public string? SearchTerm { get; set; }

    /// <summary>Restrict to one lifecycle state.</summary>
    public OrderStatus? Status { get; set; }

    /// <summary>Only orders worth at least this much.</summary>
    public decimal? MinimumTotal { get; set; }

    /// <summary>Column to sort on.</summary>
    public OrderSortField SortBy { get; set; } = OrderSortField.CreatedAt;

    /// <summary>Sort direction.</summary>
    public SortDirection SortDirection { get; set; } = SortDirection.Descending;
}
