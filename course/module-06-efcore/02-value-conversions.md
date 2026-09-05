# 3. Value conversions and strongly-typed IDs

> Part of [Module 06 — EF Core in depth](README.md), section 3.
> Previous: [2. Fluent configuration](03-fluent-configuration.md) ·
> Next: [4. The N+1 problem](04-n-plus-one.md)

---

[Value objects](../module-05-clean-architecture/02-entities-and-value-objects.md) exist so the domain
can be expressive and safe. Value conversions are how that survives contact with a database that only
knows about `nvarchar`, `int` and `uniqueidentifier`.

A conversion is two functions: **domain type → column type** on the way out, and **column type →
domain type** on the way in.

```csharp
builder.Property(o => o.OrderNumber)
    .HasConversion(
        n => n.Value,                       // OrderNumber → string
        v => OrderNumber.FromTrusted(v))    // string → OrderNumber
    .HasMaxLength(20)
    .IsRequired();
```

The column is a plain `nvarchar(20)`. Anything reading the database with SSMS sees `ORD-2026-00042`.
The C# side never handles a bare string.

## `FromTrusted`, and why it is not `Create`

Look at the read direction again. It calls `OrderNumber.FromTrusted(v)`, not
`OrderNumber.Create(v)`, and that distinction is worth understanding because it comes up in every
value object you map.

`Create` validates and returns a `Result<OrderNumber>` — the right thing for input arriving from a
user. But data coming *out of your own database* was validated when it went in. Re-validating on
every read costs time on the hot path, and — more importantly — a `Result` is the wrong shape here:
the materialiser has no sensible way to handle a failure. What would it do, skip the row?

So there are two factories with different contracts:

```csharp
public static Result<OrderNumber> Create(string value);   // untrusted input, may fail
public static OrderNumber FromTrusted(string value);      // our own data, cannot fail
```

If `FromTrusted` ever throws, the database contains something your domain says is impossible, and a
loud failure at that moment is exactly right.

## Strongly-typed ids, without the boilerplate

`OrderId`, `ProductId` and `WarehouseId` are distinct types wrapping a `Guid`, so this does not
compile:

```csharp
order.AssignWarehouse(customerId);   // ✗ cannot convert CustomerId to WarehouseId
```

The cost is that every one needs a conversion. Writing `HasConversion` for each, in every
configuration that mentions it, is dozens of identical lines and the next entity will forget one. So
it is a convention instead:

```csharp
protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
{
    configurationBuilder.RegisterStronglyTypedIds();
}
```

`RegisterStronglyTypedIds` finds every type implementing `IStronglyTypedId<TValue>` in the model and
registers the conversion once. Add a new id type and it works with no configuration at all — see
[`StronglyTypedIdConvention.cs`](../../src/LogiFlow.Infrastructure/Persistence/Conventions/StronglyTypedIdConvention.cs).

## Conversions vs owned types

Both map a value object. They are not interchangeable, and choosing wrongly is a schema you have to
migrate away from later.

| | Value conversion | Owned type (`OwnsOne`) |
|---|---|---|
| Shape | one property → **one column** | several properties → **several columns** |
| Use for | `OrderNumber`, `Sku`, `Currency`, ids | `Address`, `Money` with currency |
| Queryable | yes, on the whole value | yes, per component |
| Indexable | yes | yes, per column |

```csharp
// One column: Currency is just a 3-letter code.
builder.Property(o => o.Currency)
    .HasConversion(c => c.Code, code => Currency.FromCodeOrThrow(code))
    .HasMaxLength(3);

// Six columns: an address has parts you will want to filter and index separately.
builder.OwnsOne(o => o.ShippingAddress, address =>
{
    address.Property(a => a.Line1).HasColumnName("ShippingAddress_Line1");
    address.Property(a => a.City).HasColumnName("ShippingAddress_City");
    ...
});
```

The test: **will anyone ever query one part of it?** "All orders shipping to Modena" needs `City` as
its own column, so `Address` is an owned type. Nobody queries "the second character of the currency
code", so `Currency` is a conversion.

## The trap that costs real money

**A converted property cannot be translated into SQL beyond simple equality.**

```csharp
// ✓ Equality works: EF converts the right-hand side and emits WHERE OrderNumber = @p0
db.Orders.Where(o => o.OrderNumber == someNumber)

// ✗ This does not translate. EF cannot see inside the conversion.
db.Orders.Where(o => o.OrderNumber.Value.StartsWith("ORD-2026"))
```

Depending on the EF version you get a translation exception — or, in older configurations, silent
**client-side evaluation**: the entire `Orders` table is loaded into memory and filtered there. The
query "works", passes review, passes testing on 200 rows, and takes ninety seconds in production.

The same applies to ordering and grouping. If you need to filter on part of a converted value, that
part wants to be its own column.

**Comparisons on converted values use the *column* semantics, not the domain's.** A conversion that
serialises to JSON, for example, gives you string comparison of JSON text — which is almost never
the ordering you meant.

## `ValueComparer`, for the mutable cases

EF's change detection compares snapshots by value. For a converted property whose CLR type is a
*reference* type or a collection, the default comparison is by reference, so a mutation is invisible
and the change is never saved.

```csharp
builder.Property(p => p.Tags)
    .HasConversion(
        tags => string.Join(',', tags),
        text => text.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList())
    .Metadata.SetValueComparer(new ValueComparer<List<string>>(
        (a, b) => a!.SequenceEqual(b!),
        v => v.Aggregate(0, (hash, s) => HashCode.Combine(hash, s.GetHashCode())),
        v => v.ToList()));
```

This repository mostly avoids the problem by keeping value objects immutable — a `readonly record
struct` compares structurally and cannot be mutated — which is the better fix. Know the comparer
exists for when you inherit a model that does not.

## The mistakes

**Storing enums by name.** `HasConversion<string>()` on an enum feels friendly and means renaming a
member is a data migration. This repository uses `HasConversion<int>()` with
[pinned values](../module-05-clean-architecture/06-state-machines.md).

**Converting something you need to query into.** Covered above. The commonest real instance is a
JSON blob that later needs a filter.

**Using `Create` in the read direction.** Cost on the hot path, and no sensible failure behaviour.

**Forgetting `HasMaxLength` on a converted string.** The conversion says nothing about length, so
without it you are back to `nvarchar(max)`.

## Try it

Add this to a query and run it:

```csharp
db.Orders.Where(o => o.OrderNumber.Value.StartsWith("ORD"))
```

Read the exception carefully — it is EF telling you it cannot translate an expression over a
converted property. Then look at the SQL EF *does* produce for `o.OrderNumber == number` and note
that the parameter arrives already converted to a string.

## What to remember

- A conversion is two functions; the column stays a plain database type.
- `Create` validates untrusted input; `FromTrusted` materialises your own data and cannot fail.
- Register id conversions with a convention, or the next entity will forget one.
- One column → conversion. Several columns you might query → owned type.
- Converted properties translate for equality and little else. Never filter inside one.
- Immutable value objects avoid needing a `ValueComparer` at all.
- Store enums as `int` with pinned values, not as names.

**Code:** [`StronglyTypedIdConvention.cs`](../../src/LogiFlow.Infrastructure/Persistence/Conventions/StronglyTypedIdConvention.cs) ·
[`OrderConfiguration.cs`](../../src/LogiFlow.Infrastructure/Persistence/Configurations/OrderConfiguration.cs) ·
[`ValueObjects/`](../../src/LogiFlow.Domain/ValueObjects/)

**Next:** [4. The N+1 problem](04-n-plus-one.md)
