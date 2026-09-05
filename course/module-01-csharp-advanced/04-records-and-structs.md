# 1–2. Records, and structs that earn their place

> Part of [Module 01 — Advanced C#](README.md), sections 1 and 2.
> Next: [5. Smart enums](05-smart-enums.md)

---

C# gives you four ways to declare a type — `class`, `record`, `struct`, `record struct` — and the
choice is decided by two independent questions:

|  | **Reference** | **Value** |
|---|---|---|
| **Reference equality** | `class` | — |
| **Structural equality** | `record` | `readonly record struct` |
| **Value semantics, no equality help** | — | `struct` |

1. **Is this thing identified by *what it is* or by *which one it is*?** Structural equality, or
   reference equality.
2. **Is it small, short-lived, and copied a lot?** Value type, or reference type.

Get these right and [entities and value objects](../module-05-clean-architecture/02-entities-and-value-objects.md)
map onto the language almost mechanically.

## What `record` actually generates

```csharp
public record Address(string Line1, string City, string PostalCode);
```

The compiler writes: a public constructor, `init`-only properties, `Equals`/`GetHashCode` over **all
fields**, `==` and `!=`, `ToString()` that prints the members, a `Deconstruct`, and a protected copy
constructor for `with`.

That last one is the underrated part:

```csharp
Address corrected = original with { PostalCode = "41121" };
```

**`with` is a shallow copy.** A record holding a `List<T>` shares the same list with its copy —
mutate it through either and both see it. That surprises people who read "records are immutable":
the *record* is immutable, not what it points at. Use immutable collections if that matters.

**Equality is by value, and includes every field.** Which is exactly right for a value object and
exactly wrong for an entity — two customers with identical details are two customers. That is why
`Entity<TId>` in this repository is a `class` with hand-written equality by id.

A `record` is still a **reference type**. `record struct` is the value-type version, and `readonly
record struct` — what `Money`, `Sku` and `Weight` use — is the one you almost always want, because
it makes immutability a compiler guarantee rather than a convention.

**Inheritance and equality.** Records support inheritance, and the generated `Equals` compares the
hidden `EqualityContract` — so a `Base` and a `Derived` with identical fields are *not* equal. That
is correct, and it is the same reasoning as the `GetType()` check in `Entity<TId>`.

## When a struct earns its place

The default is a class. A struct is a deliberate optimisation with real conditions attached:

- **Small** — 16 bytes or so is the usual guidance. Above that, copying costs more than the
  indirection you saved.
- **Immutable** — a mutable struct is a bug generator (below).
- **Short-lived** — it lives on the stack or inline in its owner, so it avoids a heap allocation and
  the GC never sees it.
- **Logically a single value** — a point, an amount of money, a date.

`Money` qualifies on all four: two fields, `readonly`, created and discarded constantly inside
calculations, and unmistakably one value. In a loop summing ten thousand order lines that is ten
thousand allocations avoided — measurable, and [module 19](../module-19-memory-and-gc/) measures it.

### The mutable-struct trap

```csharp
public struct Counter { public int Value; public void Increment() => Value++; }

var list = new List<Counter> { new() };
list[0].Increment();     // ✗ does not compile — and that error is doing you a favour

var array = new Counter[1];
array[0].Increment();    // ✓ compiles, and mutates the array element

var counter = list[0];
counter.Increment();     // mutates a COPY. The list is unchanged. No warning.
```

`list[0]` returns a *copy*; `array[0]` returns a reference to the element. Same syntax, opposite
behaviour, silently. `readonly struct` makes the whole class of bug impossible, which is why every
value object here is one.

### Boxing undoes it

```csharp
Money m = new(10m, Currency.Eur);
object boxed = m;              // heap allocation — the saving is gone
IComparable c = m;             // same thing, less obviously
```

Assigning a struct to `object` or to an interface boxes it. Which is one of the three reasons
generics exist ([site chapter 04](../../site/chapters/04-generics-delegates-events.html)):
`List<Money>` stores them inline, `ArrayList` boxes every one.

`in`, `ref readonly` and `ref struct` exist for the cases where even the copy is too expensive.
`Span<T>` is a `ref struct` — stack-only, cannot be boxed, cannot be a field of a class, cannot cross
an `await`. Those restrictions are the price of never allocating.

## Choosing, in practice

| Need | Use |
|---|---|
| An entity with identity | `class` + id equality |
| A value object | `readonly record struct` if small, `record` otherwise |
| A DTO crossing a boundary | `record` |
| A command or query | `record` — immutable and value-compared for free |
| A domain event | `record` — a fact should not be mutable |
| Something big or with a mutable collection | `class` or `record` |

Every command in `Features/` is a `record`, deliberately: an operation's inputs should not change
while it is being validated and then handled.

## The mistakes

**A `record` for an entity.** Two orders with the same contents compare equal, so a `HashSet<Order>`
silently deduplicates distinct orders.

**A mutable `struct`.** Copies mutate, and the version that compiles is the one that surprises you.

**Assuming `with` deep-copies.** It does not.

**A struct because "it is faster".** Over ~16 bytes, or passed around a lot, the copying costs more
than the allocation. Measure — `dotnet run --project labs/Labs.Playground structs` does.

**Forgetting that records are reference types.** `record` is a class with generated equality.

## Try it

```bash
dotnet run --project labs/Labs.Playground structs
dotnet run --project labs/Labs.Playground boxing
```

The first prints allocation counts for the same workload as a class and as a struct; the second shows
the saving disappearing the moment a value is boxed. Then take the mutable-struct snippet above,
compile it, and note which of the three lines the compiler stops.

## What to remember

- Two questions: structural or reference equality, and value or reference semantics.
- `record` generates equality over all fields, `with`, `ToString` and `Deconstruct` — and is a class.
- `with` is shallow. Immutable records can share mutable contents.
- Structs must be small, immutable, short-lived and logically one value.
- A mutable struct mutates copies. `readonly struct` removes the whole class of bug.
- Boxing a struct undoes the saving; generics are how you avoid it.
- Records for DTOs, commands and events. Classes with id equality for entities.

**Code:** [`ValueObjects/Money.cs`](../../src/LogiFlow.Domain/ValueObjects/Money.cs) ·
[`Common/Entity.cs`](../../src/LogiFlow.Domain/Common/Entity.cs) ·
[`Features/Orders/`](../../src/LogiFlow.Application/Features/Orders/)

**Next:** [5. Smart enums](05-smart-enums.md)
