using FluentValidation;
using LogiFlow.Application.Abstractions.Data;
using LogiFlow.Application.Abstractions.Messaging;
using LogiFlow.Application.Abstractions.Services;
using LogiFlow.Domain.Customers;
using LogiFlow.Domain.Orders;
using LogiFlow.Domain.Results;
using LogiFlow.Domain.ValueObjects;

namespace LogiFlow.Application.Features.Orders;

// ─────────────────────────────────────────────────────────────────────────────────────────
//  VERTICAL SLICE: create a draft order.
//
//  Command, validator and handler sit in ONE file. That is deliberate, and it is the opposite
//  of the layer-per-folder habit (Commands/, Validators/, Handlers/, Services/).
//
//  The reason: you change these three things together, always. Splitting them across three
//  folders means three files open and three navigations for every change, and it makes
//  deleting a feature an archaeology exercise. Everything that changes together, lives
//  together. When a feature is retired, one file goes.
//
//  Covered in: course/module-08-cqrs/02-vertical-slices.md
// ─────────────────────────────────────────────────────────────────────────────────────────

/// <summary>Opens a new draft order for a customer.</summary>
/// <param name="CustomerId">Who the order is for.</param>
/// <param name="CurrencyCode">ISO-4217 code every amount on the order will use.</param>
/// <param name="ShippingAddress">Optional at draft stage; required before submission.</param>
public sealed record CreateOrderCommand(
    Guid CustomerId,
    string CurrencyCode,
    AddressDto? ShippingAddress) : ICommand<Guid>;

/// <summary>
/// Input validation for <see cref="CreateOrderCommand"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Note what is NOT checked here:</b> whether the customer exists, or whether they are
/// active. Those need a database read, which makes them business rules, and business rules
/// belong in the handler and the domain. A validator that queries the database is a validator
/// that has quietly become a handler — and one whose result is stale by the time the handler
/// runs anyway, since nothing holds a lock between the two.
/// </para>
/// <para>
/// The line is: <i>shape</i> here, <i>state</i> in the handler.
/// </para>
/// </remarks>
public sealed class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    /// <summary>Configures the rules.</summary>
    public CreateOrderCommandValidator()
    {
        RuleFor(x => x.CustomerId)
            .NotEmpty()
            .WithMessage("A customer id is required.");

        RuleFor(x => x.CurrencyCode)
            .NotEmpty()
            .Length(3)
            .WithMessage("Currency must be a three-letter ISO-4217 code.");

        // Only validate the address when one was supplied. Without the When(), a null address -
        // which is legal for a draft - would produce six spurious "required" errors.
        When(x => x.ShippingAddress is not null, () =>
        {
            RuleFor(x => x.ShippingAddress!.Line1).NotEmpty().MaximumLength(Address.MaxLineLength);
            RuleFor(x => x.ShippingAddress!.City).NotEmpty().MaximumLength(100);
            RuleFor(x => x.ShippingAddress!.PostalCode).NotEmpty().MaximumLength(20);
            RuleFor(x => x.ShippingAddress!.CountryCode).NotEmpty().Length(2);
        });
    }
}

/// <summary>Handles <see cref="CreateOrderCommand"/>.</summary>
/// <remarks>
/// <para>
/// The handler reads as a sequence of guard-and-continue steps, each returning early on failure.
/// That flatness is intentional — the alternative is nested <c>if (ok) { if (ok) { ... } }</c>
/// three levels deep, and it is much harder to verify that every path is handled.
/// </para>
/// <para>
/// Also note: no <c>SaveChangesAsync</c>. <c>TransactionBehavior</c> owns that.
/// </para>
/// </remarks>
/// <param name="customers">Customer lookup.</param>
/// <param name="orders">Order persistence.</param>
/// <param name="orderNumbers">Issues the human-facing reference.</param>
internal sealed class CreateOrderCommandHandler(
    ICustomerRepository customers,
    IOrderRepository orders,
    IOrderNumberGenerator orderNumbers) : ICommandHandler<CreateOrderCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Result<Guid>> HandleAsync(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        // 1. Parse primitives into domain types. Any failure here is a bad request, and the
        //    Result carries a specific code the client can branch on.
        Result<Currency> currency = Currency.FromCode(request.CurrencyCode);
        if (currency.IsFailure)
        {
            return currency.Error;
        }

        var customerId = CustomerId.From(request.CustomerId);

        // 2. Load state and apply the rules that need it.
        Customer? customer = await customers.GetAsync(customerId, cancellationToken).ConfigureAwait(false);
        if (customer is null)
        {
            return CustomerErrors.NotFound(customerId);
        }

        if (!customer.IsActive)
        {
            return CustomerErrors.Inactive;
        }

        // 3. Convert the optional address DTO, if one came in.
        Address? shippingAddress = null;
        if (request.ShippingAddress is { } dto)
        {
            Result<Address> address = Address.Create(
                dto.Line1, dto.Line2, dto.City, dto.Region, dto.PostalCode, dto.CountryCode);

            if (address.IsFailure)
            {
                return address.Error;
            }

            shippingAddress = address.Value;
        }

        // 4. Reserve the human-facing reference from the database sequence.
        OrderNumber orderNumber = await orderNumbers.NextAsync(cancellationToken).ConfigureAwait(false);

        // 5. Let the domain build itself. The handler never sets properties directly - if it
        //    could, the aggregate's invariants would be optional.
        Result<Order> order = Order.CreateDraft(
            orderNumber,
            customerId,
            customer.Tier,
            currency.Value,
            shippingAddress);

        if (order.IsFailure)
        {
            return order.Error;
        }

        orders.Add(order.Value);

        // Return the id, not the entity. The caller can GET it if they want the detail.
        return order.Value.Id.Value;
    }
}
