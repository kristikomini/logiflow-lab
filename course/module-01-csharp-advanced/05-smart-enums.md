# 5. Smart enums

> Part of [Module 01 — Advanced C#](README.md), section 5.
> Previous: [1–2. Records and structs](04-records-and-structs.md) ·
> Next: [7. Operator overloading and generic math](06-operator-overloading.md)

---

A C# `enum` is a named integer. That is enough surprisingly often, and it runs out in a very
specific way: **an enum cannot carry data or behaviour**, so the moment a value has properties, the
properties end up somewhere else and drift.

This repository has both patterns on purpose, and the interesting part is the boundary.

## The plain enum, done properly

```csharp
public enum CustomerTier
{
    Standard = 1,
    Silver   = 2,
    Gold     = 3,
    Platinum = 4,
}

public static class CustomerTierExtensions
{
    public static decimal DiscountRate(this CustomerTier tier) => tier switch
    {
        CustomerTier.Standard => 0.00m,
        CustomerTier.Silver   => 0.05m,
        CustomerTier.Gold     => 0.10m,
        CustomerTier.Platinum => 0.15m,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown customer tier."),
    };
}
```

Three things here are load-bearing:

**Values are pinned explicitly.** `Standard = 1`, not implicit numbering. These are persisted as
`int`, so if somebody alphabetises the members one day, every row in the database silently means
something different. Six characters prevent a data catastrophe.

**Nothing is zero.** The default value of an `enum` field is `0`, so leaving a member on zero means
"uninitialised" and "Standard" are indistinguishable. Starting at 1 makes a missing value detectable.

**A switch *expression* with a discard arm.** The compiler warns when a switch expression is not
exhaustive, so adding `Bronze` produces a warning at every decision point — the compiler telling you
where the new case needs handling. That is the safety net, and "add a `Bronze` tier and watch the
build complain" is one of the exercises in [SOLUTIONS.md](../SOLUTIONS.md).

A switch *statement* with no `default` gives you none of that. Prefer the expression.

## Where a plain enum stops being enough

Count the switches. `CustomerTier` has two — discount rate and free-shipping threshold — and they sit
next to each other in one file. That is fine.

At four or five switches over the same enum, scattered across the codebase, you have a problem the
compiler can only partially help with: adding a member warns you at each one, but there is no single
place that says *what a tier is*. The data lives in five switch statements and a developer has to
find them all to understand one concept.

That is the signal to promote it.

## The smart enum

```csharp
public sealed class Currency : IEquatable<Currency>
{
    public static readonly Currency Eur = new("EUR", "€", 2);
    public static readonly Currency Usd = new("USD", "$", 2);
    public static readonly Currency Jpy = new("JPY", "¥", 0);   // ← yen has no minor unit

    public static readonly IReadOnlyList<Currency> All = [Eur, Usd, Gbp, Jpy];

    private static readonly Dictionary<string, Currency> ByCode =
        All.ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);

    private Currency(string code, string symbol, int decimalPlaces) { … }

    public string Code { get; }
    public string Symbol { get; }
    public int DecimalPlaces { get; }

    public static Result<Currency> FromCode(string? code) => …;
}
```

**A private constructor plus static readonly instances.** The set is closed — there is no way to
construct a fifth currency — which gives you the one property an enum has that a plain class does
not.

**Each value carries its data.** `DecimalPlaces` is the reason this exists: JPY has **zero** decimal
places, so rounding to two is wrong for yen. With an enum, that fact would live in a switch far from
the currency, and every new piece of currency data would be another switch.

Look at where it is used:

```csharp
public Money(decimal amount, Currency currency)
{
    Amount = Math.Round(amount, currency.DecimalPlaces, MidpointRounding.ToEven);
}
```

`Money` asks the currency how to round itself. No switch, no `if (currency == Jpy)`, and adding a
currency changes exactly one file.

**Parsing returns a `Result`.** `Enum.Parse` on untrusted input either throws or — with
`Enum.TryParse` — happily accepts `(CustomerTier)9999`, because an enum is an integer and the runtime
does not check membership. `Currency.FromCode` cannot produce an invalid value, which is
[module 05 section 4](../module-05-clean-architecture/05-result-vs-exceptions.md) applied to parsing.

**A lookup dictionary, not a `switch`,** with `StringComparer.OrdinalIgnoreCase` — ordinal, not
culture-sensitive, which matters on a Turkish machine
([module 23](../module-23-text-culture-serialization/)).

## Choosing between them

| Use a plain `enum` when | Use a smart enum when |
|---|---|
| It is genuinely just a label | Each value carries data |
| One or two switches, close together | Behaviour differs per value |
| It is a persisted status column | The set is closed but attributes grow |
| You need `[Flags]` bit combinations | You need to parse from untrusted input safely |

`OrderStatus` stays an enum: it is a persisted status, and its behaviour lives in a
[transition table](../module-05-clean-architecture/06-state-machines.md) rather than per value.
`Currency` is a smart enum because JPY genuinely behaves differently.

## The persistence cost

A smart enum is a class, so EF needs a
[value conversion](../module-06-efcore/02-value-conversions.md):

```csharp
builder.Property(o => o.Currency)
    .HasConversion(c => c.Code, code => Currency.FromCodeOrThrow(code))
    .HasMaxLength(3);
```

Note `FromCodeOrThrow` on the read side rather than `FromCode` — data coming out of your own database
was validated going in, and a `Result` has no sensible failure behaviour inside a materialiser.

The column is `nvarchar(3)` holding `EUR`, which is also more readable in SSMS than an integer would
have been. That is a genuine secondary benefit.

## The mistakes

**Implicit enum values on a persisted enum.** Reordering members silently rewrites the meaning of
every stored row.

**A meaningful member at zero.** Indistinguishable from "not set".

**`switch` statements without `default`.** No exhaustiveness warning, so a new member is silently
ignored.

**Trusting a cast.** `(CustomerTier)99` is a legal `CustomerTier`. Validate at the boundary with
`Enum.IsDefined` or a smart enum.

**Reaching for a smart enum too early.** Two switches in one file is not a problem. The pattern costs
a value conversion, more code, and a slightly awkward `switch` (you cannot `case Currency.Eur:` on a
reference type without a pattern).

## Try it

Add a fifth currency to `Currency.All` and note what you had to change: one line. Then imagine
`DecimalPlaces` as an enum plus a switch, and count the files.

Then add `Bronze` to `CustomerTier` and run `dotnet build LogiFlow.slnx`. The switch expressions
complain at exactly the places that need a decision — which is the plain enum's version of the same
protection.

## What to remember

- An enum is a named integer: no data, no behaviour, no membership guarantee.
- Pin the values, start at 1, and never leave a meaningful member at 0.
- Prefer switch *expressions* — they warn when they are not exhaustive.
- Promote to a smart enum when values carry data or behave differently.
- Private constructor plus static instances closes the set.
- Parse to a `Result`; a cast to an enum is unchecked.
- Smart enums need a value conversion, and the read side uses the non-validating factory.
- Two switches in one file is not a reason to promote anything.

**Code:** [`ValueObjects/Currency.cs`](../../src/LogiFlow.Domain/ValueObjects/Currency.cs) ·
[`Customers/CustomerTier.cs`](../../src/LogiFlow.Domain/Customers/CustomerTier.cs)

**Next:** [7. Operator overloading and generic math](06-operator-overloading.md)
