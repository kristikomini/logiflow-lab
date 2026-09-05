# 2. Fluent configuration, not attributes

> Part of [Module 06 — EF Core in depth](README.md), section 2.
> Previous: [1. `DbContext` is a session](01-dbcontext-and-change-tracking.md) ·
> Next: [3. Value conversions and strongly-typed IDs](02-value-conversions.md)

---

There are three ways to tell EF Core how a class maps to a table: conventions, data annotations, and
the fluent API. This repository uses conventions for the defaults and the fluent API for everything
else, and uses **no data annotations at all**. That is a deliberate architectural decision, not a
style preference.

## Why not attributes

```csharp
// ✗ In LogiFlow.Domain — which is supposed to reference nothing.
[Table("Orders")]
public sealed class Order
{
    [Column(TypeName = "decimal(19,4)")]
    [MaxLength(20)]
    public string OrderNumber { get; set; }
}
```

Put those attributes on `Order` and the Domain project now references
`System.ComponentModel.DataAnnotations` and, for the EF-specific ones,
`Microsoft.EntityFrameworkCore.Abstractions`. The domain has acquired an opinion about SQL Server
column types.

The architecture test in `LayeringTests` fails on exactly this, and the reason it exists is that the
decay is gradual: one attribute is harmless, and two years later the domain cannot be tested without
a database and nobody can point at when that happened.

The practical objections are just as real:

- **Attributes cannot express most of what you need.** Composite keys, owned types, split queries,
  table splitting, filtered indexes, sequences, `rowversion` — none of them have an attribute.
- **They scatter the schema.** To answer "what indexes exist on Orders?" you read every property of
  every class. With one configuration file per entity, you read one file.
- **They cannot vary by provider.** The same domain class mapped to SQL Server and to SQLite for
  tests needs two configurations and one class.

## One file per entity

```csharp
public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.OrderNumber)
            .HasConversion(n => n.Value, v => OrderNumber.FromTrusted(v))
            .HasMaxLength(20)
            .IsRequired();

        builder.HasIndex(o => o.OrderNumber).IsUnique();
        ...
    }
}
```

…and they are discovered rather than registered:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    modelBuilder.HasDefaultSchema("logiflow");
    ...
}
```

The alternative — a wall of `modelBuilder.Entity<Order>(b => { ... })` lambdas inline — turns
`OnModelCreating` into a thousand-line method that nobody can review and every merge conflicts on.
`ApplyConfigurationsFromAssembly` means adding an entity is adding a file.

**`HasDefaultSchema("logiflow")`** rather than relying on `dbo`. In a shared database — extremely
common in the Italian *gestionale* world, where several applications sit on one SQL Server — an
explicit schema makes it obvious which tables belong to this service, and lets you grant permissions
per schema.

## Conventions: set the defaults once

The highest-value part of the whole configuration is the part that is not per-entity:

```csharp
protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
{
    configurationBuilder.RegisterStronglyTypedIds();

    configurationBuilder.Properties<string>().HaveMaxLength(500);
    configurationBuilder.Properties<decimal>().HavePrecision(19, 4);
}
```

**`string` without a length becomes `nvarchar(max)`.** That is not merely large — a `max` column
cannot be indexed normally, is stored off-row past 8000 bytes, and quietly destroys the performance
of any query that filters on it. A model-wide default means a new entity added next year gets a sane
length automatically instead of by whoever remembers.

**`decimal` without precision becomes `decimal(18,2)` and silently truncates.** EF logs a warning
that nobody reads until the accounts do not balance. `19,4` gives 15 digits before the point and 4
after, which covers unit prices in industries that genuinely need four decimal places.

That is the argument for conventions in one line: **the failure mode of forgetting is silent**, so
make forgetting impossible rather than reviewable.

## The custom convention

```csharp
configurationBuilder.RegisterStronglyTypedIds();
```

One call, and every `OrderId`, `ProductId` and `WarehouseId` in the model maps to a `Guid` column.
Without it, each strongly-typed id needs a `HasConversion` in every configuration that mentions it —
dozens of lines that are all identical and all easy to forget on the next entity.

See [`StronglyTypedIdConvention.cs`](../../src/LogiFlow.Infrastructure/Persistence/Conventions/StronglyTypedIdConvention.cs)
and [3. Value conversions](02-value-conversions.md) for how the conversion itself works.

## What belongs in a configuration

Read [`OrderConfiguration.cs`](../../src/LogiFlow.Infrastructure/Persistence/Configurations/OrderConfiguration.cs)
in full; it is a tour of the whole API. The parts worth naming here:

**Indexes, including unique ones.**

```csharp
builder.HasIndex(o => o.OrderNumber).IsUnique();
```

Application-level "check it does not exist, then insert" cannot make this guarantee — two requests
interleave and both pass the check. Only the database can. This is the same argument as
[module 07](../module-07-sql-and-transactions/): a constraint is the only correctness mechanism that
survives concurrency.

**Deliberately absent navigations.**

```csharp
builder.Property(o => o.CustomerId).IsRequired();
builder.HasIndex(o => o.CustomerId);
// No HasOne(). No navigation property.
```

EF is told about the column but not given a way to traverse from `Order` to `Customer`. That is the
[aggregate boundary](../module-05-clean-architecture/03-aggregates.md) enforced by the mapping: with
a navigation, application code could load and mutate two aggregates in one transaction, and the
boundary would be advice rather than structure.

**Owned types for value objects.**

```csharp
builder.OwnsOne(o => o.ShippingAddress, address => { ... });
```

Table splitting: `Address` has no table, and its columns live on `Orders` as
`ShippingAddress_Line1`, `ShippingAddress_City`, and so on. No join, no `AddressId`, no orphan rows —
which is right, because a value object has no identity to key a table on.

**Concurrency.**

```csharp
builder.Property(o => o.RowVersion).IsRowVersion();
```

Covered in [module 07 section 3](../module-07-sql-and-transactions/04-concurrency.md).

## The mistakes

**Mixing the two.** Annotations on some entities and fluent configuration on others means two places
to look and a real chance of contradicting yourself. Pick one; the fluent API is the one that can
express everything.

**Configuring in `OnModelCreating` directly.** It works, and it does not scale past about five
entities.

**Forgetting `ApplyConfigurationsFromAssembly` after adding a project.** The configuration compiles,
is never applied, and the entity silently falls back to conventions — usually discovered as an
`nvarchar(max)` column in production.

**Assuming conventions are enough.** They set defaults. Every non-default — an index, a unique
constraint, a precision that differs, a required relationship — still has to be stated.

## Try it

Add a `string Notes` property to `Order`, do **not** configure it, and run:

```bash
dotnet ef migrations add AddOrderNotes --project src/LogiFlow.Infrastructure --startup-project src/LogiFlow.Api
```

Read the generated migration: the column is `nvarchar(500)`, not `nvarchar(max)` — the convention
did that, and nobody had to remember. Then comment out the `HaveMaxLength(500)` convention,
regenerate, and see what you would have shipped.

## What to remember

- No data annotations in the domain: they are a reference the domain must not have.
- One `IEntityTypeConfiguration<T>` per entity, discovered with `ApplyConfigurationsFromAssembly`.
- Set model-wide defaults in `ConfigureConventions` — the failure mode of forgetting is silent.
- `string` defaults to `nvarchar(max)` and `decimal` to `(18,2)` with truncation. Both are traps.
- Unique indexes are the only guarantee that survives concurrency.
- Omitting a navigation is how you enforce an aggregate boundary in the mapping.
- `OwnsOne` splits a value object into its owner's table — no join, no orphans.

**Code:** [`Configurations/`](../../src/LogiFlow.Infrastructure/Persistence/Configurations/) ·
[`LogiFlowDbContext.cs`](../../src/LogiFlow.Infrastructure/Persistence/LogiFlowDbContext.cs)

**Next:** [3. Value conversions and strongly-typed IDs](02-value-conversions.md)
