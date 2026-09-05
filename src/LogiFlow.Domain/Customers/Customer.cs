using LogiFlow.Domain.Common;
using LogiFlow.Domain.Results;
using LogiFlow.Domain.ValueObjects;

namespace LogiFlow.Domain.Customers;

/// <summary>
/// Someone who places orders. Aggregate root.
/// </summary>
public sealed class Customer : AggregateRoot<CustomerId>
{
    private Customer(
        CustomerId id,
        string companyName,
        EmailAddress email,
        Address billingAddress,
        CustomerTier tier) : base(id)
    {
        CompanyName = companyName;
        Email = email;
        BillingAddress = billingAddress;
        Tier = tier;
        RegisteredAtUtc = DateTimeOffset.UtcNow;
    }

    private Customer()
    {
    }

    /// <summary>Trading name.</summary>
    public string CompanyName { get; private set; } = null!;

    /// <summary>Primary contact address. Unique across the system.</summary>
    public EmailAddress Email { get; private set; }

    /// <summary>Where invoices go. Not necessarily where goods go.</summary>
    public Address BillingAddress { get; private set; } = null!;

    /// <summary>Loyalty tier, driving discounts and shipping thresholds.</summary>
    public CustomerTier Tier { get; private set; }

    /// <summary>When the account was opened.</summary>
    public DateTimeOffset RegisteredAtUtc { get; private set; }

    /// <summary>Whether the customer may place new orders.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>Creates a customer.</summary>
    public static Result<Customer> Create(
        string? companyName,
        EmailAddress email,
        Address billingAddress,
        CustomerTier tier = CustomerTier.Standard)
    {
        if (string.IsNullOrWhiteSpace(companyName))
        {
            return CustomerErrors.NameRequired;
        }

        ArgumentNullException.ThrowIfNull(billingAddress);

        return new Customer(CustomerId.New(), companyName.Trim(), email, billingAddress, tier);
    }

    /// <summary>Moves the customer to a different loyalty tier.</summary>
    public Result ChangeTier(CustomerTier newTier)
    {
        if (newTier == Tier)
        {
            return Result.Success();
        }

        CustomerTier previous = Tier;
        Tier = newTier;
        Raise(new Events.CustomerTierChangedDomainEvent(Id, previous, newTier));
        return Result.Success();
    }

    /// <summary>Updates the billing address.</summary>
    public Result UpdateBillingAddress(Address address)
    {
        ArgumentNullException.ThrowIfNull(address);
        BillingAddress = address;
        return Result.Success();
    }

    /// <summary>Blocks the customer from placing further orders.</summary>
    public Result Deactivate()
    {
        if (!IsActive)
        {
            return CustomerErrors.AlreadyInactive;
        }

        IsActive = false;
        return Result.Success();
    }

    /// <summary>The discount this customer's tier entitles them to.</summary>
    public decimal DiscountRate => Tier.DiscountRate();
}

/// <summary>Failure modes for <see cref="Customer"/>.</summary>
public static class CustomerErrors
{
    /// <summary>Company name was blank.</summary>
    public static readonly Error NameRequired =
        Error.Validation("Customer.NameRequired", "Company name is required.");

    /// <summary>Customer was already deactivated.</summary>
    public static readonly Error AlreadyInactive =
        Error.Conflict("Customer.AlreadyInactive", "Customer is already inactive.");

    /// <summary>No customer exists with the requested id.</summary>
    public static Error NotFound(CustomerId id) =>
        Error.NotFound("Customer.NotFound", $"No customer found with id '{id}'.");

    /// <summary>Email address already registered.</summary>
    public static Error DuplicateEmail(EmailAddress email) =>
        Error.Conflict("Customer.DuplicateEmail", $"A customer is already registered with '{email}'.");

    /// <summary>Customer is deactivated and cannot transact.</summary>
    public static readonly Error Inactive =
        Error.Validation("Customer.Inactive", "This customer account is inactive and cannot place orders.");
}
