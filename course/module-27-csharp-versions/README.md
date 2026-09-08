# Module 27 — C# version by version

> A reference, and an argument. Every feature in this list exists because something before it was
> painful, and knowing *which* pain is what turns "I know the syntax" into "I know the language".
> Interviewers ask *"what is new in recent C#?"* constantly, and the good answer is never a list of
> features — it is one feature plus the problem it removed.

The examples marked ✅ in §3 were compiled and run against the SDK pinned in this repository
(.NET 10, C# 14). If you want to check any of them yourself, the fastest loop is
<https://sharplab.io> — paste the code and read what the compiler generated.

---

## 1. The eras, in one screen

```
   C# 1.0   2002   the language: classes, interfaces, delegates, events, properties
   C# 2.0   2005   GENERICS. Also: iterators, nullable value types, anonymous methods
   C# 3.0   2007   LINQ, and everything it needed: lambdas, extension methods, var, trees
   C# 4.0   2010   dynamic, named/optional args, generic variance (in / out)
   C# 5.0   2012   ASYNC / AWAIT
   C# 6.0   2015   the Roslyn release: ?., $"", nameof, expression bodies, exception filters
   C# 7.x   2017   tuples, pattern matching, local functions, in / readonly struct / ref struct
   C# 8.0   2019   NULLABLE REFERENCE TYPES, async streams, switch expressions, ranges
   C# 9.0   2020   records, init, top-level statements, target-typed new, relational patterns
   C# 10    2021   file-scoped namespaces, global usings, record structs
   C# 11    2022   raw strings, GENERIC MATH (static abstract), required members, list patterns
   C# 12    2023   primary constructors, collection expressions [ .. ]
   C# 13    2024   params collections, System.Threading.Lock, partial properties
   C# 14    2025   EXTENSION MEMBERS, the `field` keyword, null-conditional assignment
```

**The four that changed how C# is written**, and the ones to name if you are asked to pick:
**generics (2.0)**, **LINQ (3.0)**, **async/await (5.0)**, **nullable reference types (8.0)**.
Everything else is refinement; those four changed the shape of the code.

---

## 2. What each one was solving

**C# 2.0 — generics.** Before them, every collection was `ArrayList`: `object` in, cast out, boxing
on every value type, and cast errors at run time. `dotnet run boxing` measures what that cost —
4.5× the memory for 100,000 `int`s. Generics were not syntax sugar; they were a runtime feature
(module 22), and that is why .NET generics do not erase like Java's.

**C# 3.0 — LINQ, and the four features it required.** Extension methods (to add `Where` to
`IEnumerable` without changing it), lambdas (to pass the predicate), anonymous types and `var` (to
hold a projection), and **expression trees** (to make the lambda inspectable so a provider could
translate it to SQL). That last one is the border module 16 draws, and `dotnet run expressions`
shows the two halves side by side.

**C# 5.0 — async/await.** Before it, non-blocking I/O meant callbacks or the `Begin`/`End`
Asynchronous Programming Model, and any real logic became a nest of continuations. `await` lets the
compiler write the state machine (module 04). **C# 5 also fixed the `foreach` closure trap** —
before it, `foreach` captured one shared variable exactly as `for` still does. `dotnet run
closures` shows the two behaviours next to each other, and the reason they differ is this release.

**C# 6.0 — the small ones that add up.** `?.`, `$""`, `nameof`, expression-bodied members,
`using static`, and **exception filters** (`catch (X) when (...)`), whose two-pass semantics
`dotnet run exceptions` demonstrates. This was the first compiler written in C# — Roslyn — which
is also where analyzers and source generators come from.

**C# 7.x — patterns and low-level control.** Tuples and deconstruction; `is` patterns and `switch`
statements over types; local functions; and the performance trio: `in` parameters, `readonly
struct` (which stops defensive copies — module 01) and **`ref struct`**, which is what makes
`Span<T>` possible and why it cannot be a class field or cross an `await`.

**C# 8.0 — nullable reference types.** The billion-dollar mistake, addressed by making
nullability part of the type. Compile-time only (module 01), and the most valuable thing in the
language for a business codebase. Also: `IAsyncEnumerable<T>`, `switch` expressions, ranges and
indices, and `using` declarations.

**C# 9.0 — records.** One line for value equality, `GetHashCode`, `ToString`, `Deconstruct` and
non-destructive `with` mutation. It changed how domain models are written. `init` accessors made
immutability practical with object initializers, and relational and logical patterns (`is > 100`,
`is not null`, `or`) made `switch` expressions genuinely expressive.

**C# 10 / 11 — ceremony removal, then generic math.** File-scoped namespaces and global usings
delete a level of indentation and a screen of `using`s from every file. Then C# 11 added
**`static abstract` interface members**, which finally let generic code call a static member on
`T` — the foundation of `IAdditionOperators<,,>` and of 📂 [`Money.cs`](../../src/LogiFlow.Domain/ValueObjects/Money.cs).
`required` members made "you must set this" a compile error, and raw string literals (`"""`) made
embedded JSON and SQL readable.

**C# 12 — primary constructors and collection expressions.** A primary constructor on any class
removes the field-plus-assignment boilerplate that dependency injection generates by the hundred.
`[1, 2, .. rest]` unified array, list, span and custom-collection initialisation behind one syntax.

**C# 13 — the practical ones.** `params` on any collection type (not just arrays, so no
allocation), and **`System.Threading.Lock`** — a real lock type instead of locking on `object`,
which makes the "never lock on a shared reference" rule from module 21 enforceable by the type
system. `partial` properties, so a source generator can implement one.

**C# 14 — extension members.** Extension *methods* have existed since 2007; now a type can be
extended with **properties, static members and operators** too, declared in an `extension` block:

```csharp
public static class SequenceExtensions
{
    extension<T>(IEnumerable<T> source)
    {
        public bool IsEmpty => !source.GetEnumerator().MoveNext();   // an extension PROPERTY
    }
}
```

And the **`field` keyword**, which removes the most common piece of boilerplate in C#: a property
that needs a tiny bit of logic no longer needs a backing field you have to name and keep in sync.

```csharp
public string Name
{
    get => field ?? "";
    set => field = value?.Trim();      // `field` IS the compiler-generated backing field
}
```

Plus **null-conditional assignment** (`customer?.Name = x` — the assignment simply does not happen
if `customer` is null) and `nameof` over an unbound generic (`nameof(List<>)`).

---

## 3. The version-to-runtime map

This is the part people get wrong in interviews, so it is worth stating precisely:

> **The C# version is chosen by the target framework, not by the SDK.** `net10.0` implies C# 14.
> Setting `<LangVersion>` manually to something newer than your TFM supports is not supported and
> will fail on features that need runtime or library support.

| C# | Ships with | Notes |
|---|---|---|
| 8.0 | .NET Core 3.x | last version usable from .NET Framework (partially, unsupported) |
| 9.0 | .NET 5 | |
| 10 | .NET 6 | LTS |
| 11 | .NET 7 | |
| 12 | .NET 8 | LTS — the version most Italian shops are actually on |
| 13 | .NET 9 | |
| 14 | .NET 10 | LTS — what this repository targets, pinned in `global.json` |

📂 [`global.json`](../../global.json) pins the **SDK**, which makes the build reproducible.
📂 [`Directory.Build.props`](../../Directory.Build.props) sets the **TargetFramework**, which is
what actually chooses the language version.

**Worth knowing for the market you are interviewing in:** a lot of production code in
Emilia-Romagna manufacturing IT is on .NET 8 (LTS), and a meaningful amount is still on .NET
Framework 4.8 behind a migration plan. Knowing what is available in C# 12 versus C# 14 — and
being able to say "that pattern needs C# 11, so on .NET 6 you would write it this way instead" —
is a more useful thing to demonstrate than knowing the newest feature.

---

## 4. Do this

```bash
cd labs/Labs.Playground
dotnet run closures      # the foreach fix that arrived in C# 5, next to the for loop that did not
dotnet run boxing        # the problem generics were added in C# 2.0 to solve
dotnet run expressions   # the C# 3.0 feature that makes EF Core possible
```

Then, and this is the exercise that pays:

1. **Open <https://sharplab.io> and paste a `record`.** Read the generated `Equals`,
   `GetHashCode`, `PrintMembers` and `<Clone>$`. Every rule about records in module 01 is
   right there in the output.
2. **Paste an `async` method** and switch the output to C#. The state machine, the `MoveNext`
   switch, the awaiter fields — module 04 stops being a metaphor.
3. **Paste a lambda that captures a loop variable.** Find the compiler-generated display class.
   The closure trap in module 02 becomes an object with a field.
4. **Pick one file in `src/` and rewrite it as C# 7 would have needed.** No records, no switch
   expressions, no nullable reference types, no file-scoped namespace. Count the lines. That
   difference is what the last decade of the language bought.

---

## 5. Golden rules

1. **The C# version is chosen by the target framework, not the SDK.** `net10.0` means C# 14;
   `global.json` pins the SDK for build reproducibility, and the TFM picks the language.
2. **The four that changed the language are generics, LINQ, async/await and nullable reference
   types.** Everything else is refinement.
3. **Generics were added for boxing and type safety, not syntax** — and unlike Java's they are a
   runtime feature, which is why `typeof(List<int>)` exists.
4. **LINQ needed four features to exist**: extension methods, lambdas, anonymous types and
   expression trees. The last one is the entire C#-to-SQL border.
5. **`foreach` was fixed in C# 5; `for` was not.** A `for` loop still captures one shared variable
   — and it is still the loop you write when building a pipeline.
6. **Exception filters (`when`) run before the stack unwinds.** That is why they preserve a better
   crash dump than catch-log-rethrow.
7. **`readonly struct` and `ref struct` are performance features with semantics**: one stops
   defensive copies, the other guarantees stack-only, which is what makes `Span<T>` safe.
8. **Nullable reference types are compile-time only** — the single most valuable feature for a
   business codebase, and it still does not check anything at run time.
9. **`static abstract` interface members enable generic math, and cannot appear in an expression
   tree.** That collision is where EF Core meets modern C#.
10. **`System.Threading.Lock` (C# 13) makes "do not lock on a shared object" a type-level rule**
    rather than a convention nobody enforces.
11. **The `field` keyword (C# 14) removes the backing field**, which is the most common
    boilerplate left in the language.
12. **Know what the version you are interviewing for supports.** Being able to say "that needs C#
    11, so on .NET 6 you would write it this way" is worth more than knowing the newest feature.

---

## 6. Interview questions

**"What is new in recent C#?"**
Do not list. Pick one and say what it removed: *"C# 14 added extension members, so a type can now
be extended with properties and static members, not just methods — and the `field` keyword removed
the backing field you used to have to declare for any property with a line of logic in it."* Then
offer a second at a different level: records, or generic math.

**"Why were generics added?"**
Type safety and boxing. Before them, `ArrayList` meant `object` in and a cast out, so errors moved
to run time and every value type allocated. And .NET generics are a runtime feature rather than
erasure, which is why value-type instantiations get their own specialised code with no boxing.

**"What did LINQ need from the language?"**
Extension methods, lambdas, anonymous types with `var`, and expression trees. The first three make
the query readable; the fourth makes it *translatable*, which is the whole of `IQueryable`.

**"What problem do nullable reference types solve, and what do they not?"**
They move a large class of `NullReferenceException` to compile time by making nullability part of
the type. They do not check anything at run time — so data crossing a boundary from JSON, a
database, or an unannotated library can still be null despite the type saying otherwise.

**"Records versus classes?"**
A record is a reference type with generated value equality, `ToString`, `Deconstruct` and `with`.
Use it when the values define the thing — a value object, a DTO, an event. Use a class when
identity does. And know the `EqualityContract` clause: a derived record is never equal to its base.

**"Your team is on .NET 6. What do you lose?"**
C# 10, so no raw string literals, no `required` members, no list patterns, no generic math via
`static abstract`, no primary constructors on classes, and no collection expressions. You still
have records, nullable reference types, `switch` expressions and file-scoped namespaces — which is
most of what makes modern C# readable.

---

## Next

→ [Module 28 — Industrial software and the IT/OT boundary](../module-28-industrial-and-ot/)

The last module, and the one furthest from the language: the layer where software meets machines,
which is where a large share of the .NET work in this region actually is.
