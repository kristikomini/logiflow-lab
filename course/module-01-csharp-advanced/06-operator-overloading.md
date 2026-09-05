# 7. Operator overloading and generic math

> Part of [Module 01 — Advanced C#](README.md), section 7.
> Previous: [5. Smart enums](05-smart-enums.md) · Back to [the module](README.md)

---

Operator overloading has a bad reputation, earned by people who overloaded `+` to mean "save to
database". The rule that makes it safe is narrow and easy to apply:

> **Overload an operator only when the type is genuinely a number, a set, or a value with an
> established mathematical meaning — and only when the meaning is the one everybody already
> expects.**

`Money + Money` passes. `Order + OrderLine` does not, however convenient it would be.

## The case for it

Without operators, arithmetic on a value object reads like assembly:

```csharp
Money subtotal = Money.Zero(currency);
foreach (OrderLine line in lines)
    subtotal = subtotal.Add(line.LineTotal);

Money total = subtotal.Subtract(discount).Add(shipping);
```

With them, it reads like the domain:

```csharp
Money subtotal = _lines.Aggregate(Money.Zero(Currency), (total, line) => total + line.LineTotal);
Money total    = Subtotal - DiscountAmount + ShippingCost;
```

The second is the arithmetic a finance person would write on paper, which is the point. Expressive
code is not decoration — the total calculation in `Order` is a business rule and it should be
readable as one.

## Implementing them

```csharp
public readonly record struct Money : IComparable<Money>
{
    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount + right.Amount, left.Currency);
    }

    public static Money operator -(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount - right.Amount, left.Currency);
    }

    public static Money operator -(Money value) => new(-value.Amount, value.Currency);   // unary

    public static Money operator *(Money left, decimal multiplier) =>
        new(left.Amount * multiplier, left.Currency);

    public static Money operator *(decimal multiplier, Money right) => right * multiplier;  // both ways
}
```

Four things worth copying:

**`EnsureSameCurrency` throws rather than returning a `Result`.** Adding euros to dollars is not a
user error — no user typed it — it is a programmer error, and
[the rule](../module-05-clean-architecture/05-result-vs-exceptions.md) says programmer errors throw.
An operator cannot return a `Result` anyway, which is a real constraint on where operators are
appropriate: **if the operation can fail for ordinary reasons, it should be a method.**

**`Money * decimal` and not `Money * Money`.** Multiplying two amounts of money is meaningless —
€10 × €10 is not €100, it is nothing. Omitting the operator is a design statement.

**Both orders of the mixed operand.** `2 * price` and `price * 2` should both compile; C# does not
generate the mirror for you.

**Unary negation is separate** from binary subtraction, and easy to forget.

## The rules

**Symmetry.** Overload `==` and you must overload `!=`, and override `Equals` and `GetHashCode` to
agree — the compiler enforces the operator pair and the analysers enforce the rest. Disagreement here
is the collection-corruption bug from [module 20](../module-20-equality-and-collections/).

**Ordering comes in fours.** `<`, `>`, `<=`, `>=`, and they should agree with `CompareTo`.

**No side effects.** An operator must be a pure function. Nobody expects `a + b` to write to a
database, and nobody will look there when it does.

**Never surprise.** If a reader has to check what `+` means on your type, do not overload it.

## The conversion operators, and one strong preference

```csharp
public static implicit operator Result<TValue>(TValue value) => Success(value);
public static implicit operator Result<TValue>(Error error)  => Failure<TValue>(error);
```

That is the `Result` type in this repository, and those two implicit conversions are what make the
whole error-handling discipline pleasant instead of tedious — `return OrderErrors.EmptyOrder;`
instead of `return Result<Order>.Failure(OrderErrors.EmptyOrder);`.

The guidance:

- **`implicit`** only when the conversion **cannot fail and loses nothing**. `Error → Result` is
  always valid.
- **`explicit`** when it can fail or truncate, so the reader sees a cast and knows something is
  happening.

The classic mistake is an implicit conversion from a value object to its primitive —
`implicit operator string(Sku sku)`. It undoes the entire reason the type exists, because now a
`Sku` silently becomes a `string` at any call site expecting one, and primitive obsession is back
with extra steps.

## Generic math

```csharp
public readonly record struct Money :
    IComparable<Money>,
    IAdditionOperators<Money, Money, Money>,
    ISubtractionOperators<Money, Money, Money>,
    IMultiplyOperators<Money, decimal, Money>,
    IUnaryNegationOperators<Money, Money>
{ … }
```

Since C# 11, interfaces can declare **static abstract members**, and the BCL ships a family of them
for operators. Implementing `IAdditionOperators<Money, Money, Money>` states in the type system that
`Money + Money` is a `Money`.

Why that is worth doing:

```csharp
// Without generic math: one overload per numeric type, or `dynamic`, or nothing.
public static T Sum<T>(IEnumerable<T> values) where T : IAdditionOperators<T, T, T>, IAdditiveIdentity<T, T>
{
    T total = T.AdditiveIdentity;
    foreach (T value in values) total += value;
    return total;
}
```

That one method now works for `int`, `double`, `decimal` **and `Money`**, with no overloads and no
boxing. Before C# 11 this was impossible — you could not call an operator on an unconstrained `T`,
which is why `Enumerable.Sum` has a dozen hand-written overloads and none of them work on your types.

The three type arguments are `<TLeft, TRight, TResult>`, which is what lets
`IMultiplyOperators<Money, decimal, Money>` say "money times a plain number is money" — asymmetric,
and correct.

`static abstract` members are the same mechanism as `IParsable<T>` and `ISpanParsable<T>`, which is
how `T.Parse` became possible in generic code.

## The mistakes

**Overloading for cleverness.** `+` meaning "append to a collection and persist" is a real thing
people have shipped.

**`==` without `Equals`.** They disagree, and `Contains` on a list stops matching what `==` says.

**An implicit conversion that can fail.** It throws in the middle of an expression with no cast to
point at.

**An implicit conversion to the primitive.** Defeats the value object.

**Overloading on a mutable type.** `a + b` returning a mutated `a` is the surprise to end them all.

## Try it

Open [`Money.cs`](../../src/LogiFlow.Domain/ValueObjects/Money.cs) and try:

```csharp
Money euros   = new(10m, Currency.Eur);
Money dollars = new(10m, Currency.Usd);
Money broken  = euros + dollars;      // throws, loudly, at the point of the mistake
```

Then delete `EnsureSameCurrency` and run it again. It returns €20 — a number that is wrong, in the
right type, that will travel all the way to an invoice. That is the whole argument for putting the
check inside the operator.

## What to remember

- Overload only where the meaning is already universally understood.
- If the operation can fail for ordinary reasons, make it a method — operators cannot return a `Result`.
- Programmer errors inside an operator throw. That is the correct behaviour.
- `==` and `!=` together, agreeing with `Equals` and `GetHashCode`; ordering comes in fours.
- Provide both operand orders for mixed-type operators.
- `implicit` only when it cannot fail and loses nothing; otherwise `explicit`.
- Never convert implicitly to the underlying primitive — it undoes the value object.
- Generic math (`static abstract` members) makes one algorithm work for `int` and for `Money`.

**Code:** [`ValueObjects/Money.cs`](../../src/LogiFlow.Domain/ValueObjects/Money.cs) ·
[`Results/Result.cs`](../../src/LogiFlow.Domain/Results/Result.cs)

**Back to:** [Module 01](README.md)
