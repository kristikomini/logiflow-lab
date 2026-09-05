# Module 01 — Advanced C#

> The language features that show up in every senior interview, taught from code that uses them
> for a reason rather than to demonstrate them.

---

## Deeper chapters

Some sections below have a chapter that goes further — the code, the traps, and the interview answer.

| | Chapter | |
|---|---|---|
| 1–2 | [Records, and structs that earn their place](04-records-and-structs.md) | four ways to declare a type, and the mutable-struct trap |
| 5 | [Smart enums](05-smart-enums.md) | when a named integer stops being enough |
| 7 | [Operator overloading and generic math](06-operator-overloading.md) | the narrow rule that makes it safe |

---

## 1. Records

```csharp
public sealed record Address(string Line1, string? Line2, string City, ...);
```

One line gives you value equality, `GetHashCode`, `ToString`, `Deconstruct`, and non-destructive
mutation via `with`. That is the single best argument for records in a domain model.

📂 [`Domain/ValueObjects/Address.cs`](../../src/LogiFlow.Domain/ValueObjects/Address.cs)

**`record` vs `class`:** a record's `Equals` compares fields; a class's compares references.
Use a record when the *values* define the thing (a value object, a DTO, an event). Use a class
when identity does (an entity).

**A detail worth knowing:** record equality includes an `EqualityContract` type check, so a
derived record never compares equal to its base even with identical fields. That is why
`ValidationError` never compares equal to a plain `Error` with the same code — see
[`Domain/Results/Error.cs`](../../src/LogiFlow.Domain/Results/Error.cs).

---

## 2. Structs, and when they earn their place

```csharp
public readonly record struct OrderId(Guid Value);
```

📂 [`Domain/Common/Ids.cs`](../../src/LogiFlow.Domain/Common/Ids.cs)

- **`struct`** — no heap allocation. 16 bytes, exactly like the `Guid` inside.
- **`readonly`** — the compiler stops defensive copies on member access.
- **`record`** — value equality for free.

**Use a struct when:** it is small (≤16 bytes is the usual guidance), immutable, and short-lived
or allocated in bulk. **Use a class otherwise** — a large struct is copied on every assignment,
argument pass and return, which is worse than an allocation.

### The picture worth holding in your head

```
   class Order  (reference type)          struct OrderId  (value type)
   ──────────────────────────────         ──────────────────────────────
   STACK            HEAP                  STACK
   order ─────────► ┌─────────────┐       id    [ Guid · 16 bytes ]
   copy  ─────────► │ Order       │       copy  [ Guid · 16 bytes ]  ← a real, separate copy
                    └─────────────┘
   copying copies the ARROW                copying copies the BYTES
   both names see one object               the two names can never disagree
```

And the choice, as a ladder:

```
   Does identity matter — is it still "the same one" after every field changes?
        │ yes ─────────────────────────────────►  class          Order, Customer
        │ no
   Is it small (≈16 bytes), immutable, allocated in bulk?
        │ yes ─────────────────────────────────►  readonly record struct   OrderId, Sku
        │ no
        └──────────────────────────────────────►  record         Address, DTOs, events
```

### The mutable-struct trap

```bash
cd labs/Labs.Playground && dotnet run structs
```

You will see that `list[0].Increment()` changes nothing (a `List<T>` indexer returns a *copy*)
while `array[0].Increment()` works (an array indexer gives a reference to the slot). Same syntax,
opposite behaviour. This is why mutable structs are discouraged — make them `readonly` and the
compiler stops you writing the bug.

---

## 3. Nullable reference types

`<Nullable>enable</Nullable>` turns a whole class of `NullReferenceException` into a compile-time
error. `string` means "never null"; `string?` means "might be".

Things worth knowing:

- **It is compile-time only.** Nothing is checked at runtime, so data crossing a boundary
  (JSON, a database, an old library) can still be null despite the type.
- **`= null!`** is the null-forgiving operator: "trust me". Used in this codebase only for EF
  Core-materialised properties, where the ORM guarantees assignment but the compiler cannot see
  it — e.g. `public string Name { get; private set; } = null!;`
- **`[NotNullWhen(true)]`** teaches the compiler about your own methods.
  📂 See `Result<T>.TryGetValue`, where it means correct code needs no `!`.

---

## 4. Pattern matching

Modern C# lets you express conditions declaratively.

```csharp
if (Status is OrderStatus.Shipped or OrderStatus.Delivered) { }        // or-pattern

int status = error.Type switch                                          // switch expression
{
    ErrorType.Validation  => 400,
    ErrorType.NotFound    => 404,
    ErrorType.Conflict    => 409,
    _                     => 500,
};

if (order.FulfillingWarehouseId is { } warehouseId) { ... }             // property pattern + capture

exception switch
{
    DbUpdateException e when IsUniqueViolation(e) => 409,               // guard clause
    ...
};
```

📂 [`Api/Infrastructure/GlobalExceptionHandler.cs`](../../src/LogiFlow.Api/Infrastructure/GlobalExceptionHandler.cs)

**The trick worth stealing:** a `switch` expression over an enum with **no `default` arm** makes
the compiler warn when the switch stops being exhaustive. With `TreatWarningsAsErrors`, adding
an enum member without handling it *fails the build*.

📂 [`Domain/Customers/CustomerTier.cs`](../../src/LogiFlow.Domain/Customers/CustomerTier.cs) — adding a
tier without deciding its discount is impossible. Adding `default => 0m` would throw that safety
net away.

---

## 5. Smart enums

A C# `enum` is a named integer. It cannot carry behaviour or data, and `(Currency)999` is a
perfectly legal value that matches no case.

📂 [`Domain/ValueObjects/Currency.cs`](../../src/LogiFlow.Domain/ValueObjects/Currency.cs)

`Currency` is a sealed class with static instances, so each one carries its symbol and decimal
places. That matters: **JPY has zero decimal places**, and a plain enum has nowhere to put that
fact — so it ends up in a `switch` in a static helper, then a second one for the symbol, then a
third somewhere else.

**When to use which:** enum for a simple closed set with no attached data (`OrderStatus`); smart
enum when behaviour or data belongs to each member (`Currency`). Knowing which to reach for is
the actual skill.

**Persisted enums need explicit values.** If someone inserts `Bronze` alphabetically between
`Gold` and `Silver` without pinned values, every existing row silently changes meaning.

---

## 6. Generics and static abstract members

C# 11 added `static abstract` interface members, which finally let generic code call a static
method on `T`:

```csharp
public interface IStronglyTypedId<out TSelf> where TSelf : struct
{
    Guid Value { get; }
    static abstract TSelf From(Guid value);
}
```

📂 [`Domain/Common/IStronglyTypedId.cs`](../../src/LogiFlow.Domain/Common/IStronglyTypedId.cs)

Before them, generic code could not say "call the static factory on T", so every ID type needed
hand-written glue.

### The limitation you will hit

`TId.From(value)` **cannot appear in an expression tree**:

```
CS8927: An expression tree may not contain an access of static virtual or abstract interface member
```

Static abstracts are resolved per closed generic type at JIT time; an expression tree is built at
compile time, so there is no method handle to embed. This bites whenever generic math or static
abstracts meet EF Core, a LINQ provider, or a mocking library.

📂 [`Infrastructure/.../StronglyTypedIdConvention.cs`](../../src/LogiFlow.Infrastructure/Persistence/Conventions/StronglyTypedIdConvention.cs)
shows the fix: build the tree by hand with `Expression.New`.

---

## 7. Operator overloading and generic math

📂 [`Domain/ValueObjects/Money.cs`](../../src/LogiFlow.Domain/ValueObjects/Money.cs)

`Money` implements `IAdditionOperators<Money, Money, Money>` and friends (C# 11 generic math), so
it can be used from generic algorithms constrained on arithmetic.

Note that `operator +` **throws** on a currency mismatch rather than returning a `Result` — and
the file explains why. A user cannot make the system add euros to yen; only a developer wiring
the wrong field can. That is a bug, not a business outcome.

---

## 8. Two things that produce measurable wins

**Source-generated regex** — `[GeneratedRegex]` (.NET 7+) emits a purpose-built matcher at
compile time: 3–10× faster, no allocation, no first-call JIT cost, and an invalid pattern becomes
a *compile* error instead of a runtime `RegexParseException`.
📂 [`Domain/ValueObjects/Sku.cs`](../../src/LogiFlow.Domain/ValueObjects/Sku.cs)

**`Guid.CreateVersion7()`** instead of `Guid.NewGuid()` — v7 GUIDs are time-ordered in their high
bits, so rows append at the end of a clustered index instead of scattering page splits across it.
On SQL Server that is a real insert-throughput difference at scale.

---

## 9. Do this

```bash
cd labs/Labs.Playground
dotnet run structs      # value vs reference, and the mutable-struct trap
dotnet run strings      # interning, == vs ReferenceEquals, StringBuilder allocation
dotnet run boxing       # where hidden allocations come from
```

And four that go deeper into this module's material than the module does:

```bash
dotnet run equality     # the five kinds of equality, and which one your code just used
dotnet run numbers      # decimal vs double, silent overflow, and banker's rounding
dotnet run variance     # why array covariance throws at run time and IEnumerable<out T> does not
dotnet run generics     # what the JIT does with T, and where a constraint removes the boxing
```

Records, structs and pattern matching are the *surface*. [Module 20](../module-20-equality-and-collections/)
is the equality contract underneath them, and [module 22](../module-22-clr-internals/) is what the
runtime does with the generics.

---

## 10. Golden rules

> The card. These are the sentences that come out under interview pressure.

1. **Record for values, class for identity, struct for small immutable data.** `readonly record
   struct` is all three properties at once, and it is what a strongly-typed ID should be.
2. **A struct larger than ~16 bytes costs more than an allocation**, because it is copied on every
   assignment, argument pass and return.
3. **`readonly` on a struct is not decoration.** It is what stops the compiler making a defensive
   copy on every member access through a readonly field or an `in` parameter.
4. **Nullable reference types are compile-time only.** Nothing is checked at runtime, so you still
   validate at every boundary: JSON, the database, an unannotated library.
5. **A `switch` expression over an enum with no `default` arm is a tripwire.** Add a member without
   handling it and the build fails. Adding `default => …` throws that away.
6. **Enum for a closed set with no data; smart enum the moment a member carries behaviour or a
   value** — like JPY having zero decimal places.
7. **Pin the numeric values of any enum you persist.** Insert a member alphabetically without them
   and every existing row silently changes meaning.
8. **Static abstract interface members cannot appear in an expression tree.** When generic math or
   a static factory meets EF Core or a mocking library, build the tree by hand with
   `Expression.New`.
9. **Throw for a developer mistake, return a value for a user's.** `Money + Money` in the wrong
   currency is a wiring bug, not a business outcome — so it throws.

---

## 11. Interview questions

**"`record` vs `class` vs `struct`?"**
Record = reference type with value equality, for values and DTOs. Class = reference type with
reference equality, for entities. Struct = value type, no allocation, copied on assignment — for
small immutable values. `readonly record struct` gets you all three properties at once.

**"What does `readonly` on a struct do?"**
Guarantees no member mutates state, which lets the compiler skip defensive copies when the struct
is accessed through a readonly field or `in` parameter. Without it, every such access copies.

**"Are nullable reference types enforced at runtime?"**
No — compile-time analysis only. Data from JSON, a database, or a non-annotated library can still
be null. That is why you still validate at boundaries.

**"What is boxing and why does it matter?"**
Wrapping a value type in a heap object to treat it as `object`. It allocates, and the allocation
is invisible in the source. It is why generics were added in C# 2.0. Run
`dotnet run boxing` for the numbers: 4.5 MB vs 1 MB for 100,000 ints.

**"What are static abstract interface members for?"**
They let generic code call static members on the type parameter — factory methods, operators,
parsing. They power generic math. The catch: they cannot be used inside expression trees.

---

## Next

→ [Module 02 — Delegates, lambdas and closures](../module-02-delegates-and-closures/)
