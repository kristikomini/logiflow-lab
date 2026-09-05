# Module 22 — Inside the CLR: compilation, types, and the JIT

> What actually happens between `dotnet build` and your code running. This is the module that
> makes the other modules stop being a list of rules — once you can see the pipeline, most of
> .NET's behaviour becomes obvious rather than memorised.

```bash
cd labs/Labs.Playground
dotnet run generics     # what the JIT does with T
dotnet run reflection   # the metadata lookup, the boxing, and the cache that kills both
dotnet run expressions  # code as data
```

---

## 1. The pipeline

```
   Program.cs                 your source
        │  Roslyn (the C# compiler)   ── analyzers and SOURCE GENERATORS run here
        ▼
   LogiFlow.Api.dll           IL + METADATA, in a PE file. Portable, not machine code.
        │  the runtime loads it, resolves types, builds method tables
        ▼
   JIT, per method, on first call        ── tier 0 fast, then tier 1 optimised
        ▼
   machine code for THIS cpu, cached for the life of the process
```

Four things follow from that diagram, and they explain a lot:

- **A .dll is not machine code.** It is IL plus metadata — a complete, queryable description of
  every type, member and signature. That is why reflection exists at all, why a decompiler like
  ILSpy can reconstruct readable C#, and why "compiled" is not a security measure.
- **Compilation happens per method, on first call.** This is why the first request to an endpoint
  is slow and the second is not, and why measuring elapsed time in a plain console app measures
  the JIT (module 14 says this too, from the other end).
- **The JIT knows your exact CPU.** It can use AVX-512 on the machine it is running on, which an
  ahead-of-time compiler targeting "x64" cannot. That is the trade you make with AOT.
- **`TargetFramework` is a compile-time contract; the installed runtime is what executes.**
  `global.json` pins the SDK so the *build* is reproducible; the runtime version is a deployment
  decision.

**Tiered compilation** is the reason startup got fast without giving up steady-state speed:

```
   tier 0     compiled fast, barely optimised, with a call counter
      │       after ~30 calls, or a loop iterating a lot (OSR)
      ▼
   tier 1     fully optimised — inlining, bounds-check elimination, vectorisation
```

**On-Stack Replacement (OSR)** handles the awkward case: a method entered once that then loops a
million times would otherwise be stuck at tier 0 forever. OSR swaps the optimised version in
*while the loop is running*.

**ReadyToRun** precompiles a baseline native version into the assembly, so startup skips tier 0 —
and the JIT still re-optimises hot methods at tier 1. Bigger file, faster start, no loss of peak
speed. It is the right default for a container that gets restarted.

**Native AOT** compiles everything ahead of time and ships no JIT at all: startup in
milliseconds, a fraction of the memory, a single self-contained binary. The price is absolute:
**no runtime code generation**. No `Reflection.Emit`, no `Expression.Compile`, no dynamic
assembly loading — and any reflection over a type nothing references statically may have been
trimmed away, so it throws at run time rather than failing to build. Practically: AOT is ideal for
a small, focused API or a CLI tool, and is still awkward for an EF Core application, because EF's
query pipeline is built on expression trees.

---

## 2. Assemblies, metadata, and what `typeof` costs

An assembly is one deployment unit and one *versioning* unit. Inside it:

```
   ┌─ manifest ── name, version, culture, public key, referenced assemblies
   ├─ metadata ── every type, method, field, property, parameter, attribute
   ├─ IL        ── the method bodies
   └─ resources ── embedded files
```

**`AssemblyLoadContext`** replaced AppDomains in .NET Core. It is how plugin systems load and —
crucially — *unload* assemblies, and how two versions of the same library can coexist. It is also
where "my plugin will not unload" bugs live: a single lingering reference to any type in the
context keeps the whole thing loaded.

**`InternalsVisibleTo`** is the mechanism the test projects here use to reach `internal` types
without making them public. 📂 See [`Directory.Build.props`](../../Directory.Build.props).

---

## 3. How an object knows what it is

Every reference-type instance carries two hidden words before its fields:

```
   ┌──────────────────────────┐
   │ sync block index (8 B)   │  ← lock state, hash code, and other rarely-used bits
   │ method table ptr (8 B)   │  ← "what type am I", and where my methods are
   ├──────────────────────────┤  ← the reference you hold points HERE
   │ fields...                │
   └──────────────────────────┘
```

The **method table** is the type's identity, its static fields, and its virtual dispatch table.
That one pointer is the mechanism behind several things you already use:

- **`GetType()`** simply reads it. That is why it is fast and why it cannot lie — unlike a
  compile-time type, which can be a base class.
- **Virtual dispatch** is an indirect call through that table. **Interface dispatch** is one more
  step and is why `sealed` classes and non-virtual methods let the JIT devirtualise and then
  inline a call.
- **`lock (obj)`** uses the sync block, which is why every object can be locked on and why
  locking on a shared instance is so easy to get wrong (module 21).
- **Boxing** allocates one of these and copies the struct's bytes into the fields region. The box
  is a *different object* from the value, which is the whole reason `dotnet run boxing` shows
  what it does.

---

## 4. Generics, and what makes them different in .NET

Java erases generics; C++ templates duplicate everything at compile time. The CLR does neither —
it keeps generics **in the metadata** and instantiates at run time:

```
   List<int>     ──► its own machine code, int stored inline, no boxing
   List<long>    ──► its own machine code again
   List<string>  ──┐
   List<Order>   ──┼─► ONE shared "canonical" body, used by every reference type
   List<Customer>──┘
```

Consequences worth carrying:

- **Real type safety at run time**, not just at compile time. `typeof(List<int>)` is a real,
  distinct type, which is why `List<T>` can be serialized, reflected over, and constrained.
- **No boxing for value types**, which was the entire point of adding generics in C# 2.0.
  `dotnet run boxing` measures the difference against `ArrayList`.
- **A static field in a generic type is per closed type.** `Counter<int>.Instances` and
  `Counter<string>.Instances` are two different fields. This is the mechanism behind the per-type
  caches inside serializers, DI containers and mediators — and `dotnet run generics` prints it.
- **Every value-type instantiation costs its own JIT time and code size.** Reference types share.
  This is why a heavily generic library over many struct types is slow to start under Blazor
  WebAssembly and Native AOT.
- **A generic constraint eliminates boxing that an interface parameter forces.**
  `void M<T>(T x) where T : IShout` compiles to a direct call on a struct;
  `void M(IShout x)` boxes first. The demo measures 240,000 bytes versus zero.

**`static abstract` interface members** (C# 11) finally let generic code call a static member on
`T` — factories, operators, parsing. They are what generic math is built on. The limitation is
covered in module 01 and it comes straight from this module: they are resolved *per closed type
at JIT time*, and an expression tree is built at compile time, so there is no method handle to
put in the tree. 📂 [`StronglyTypedIdConvention.cs`](../../src/LogiFlow.Infrastructure/Persistence/Conventions/StronglyTypedIdConvention.cs)
shows the workaround.

---

## 5. Reflection, and the two things that actually cost

`dotnet run reflection` separates them, because conflating them is how the folklore "reflection is
slow" survives:

1. **The metadata lookup.** `typeof(T).GetProperty("Name")` searches by string every time.
2. **The boxing.** `PropertyInfo.GetValue` returns `object`, so reading an `int` allocates. The
   demo measures 2.4 MB for 100,000 reads.

Cache the `MemberInfo` and both shrink; build a delegate and the cost essentially disappears:

```csharp
// once, at startup, into a static cache
Func<Row, string> get = (Func<Row, string>)Delegate.CreateDelegate(
    typeof(Func<Row, string>), property.GetGetMethod()!);
```

That is precisely what a mediator, an ORM and a serializer do internally, and it is why they are
not as slow as their use of reflection would suggest.

**Attributes are metadata, and reading them is reflection.** `GetCustomAttributes` allocates and
searches; if you read the same attribute per request, cache it per type in a `static
Dictionary<Type, T>` or a `ConcurrentDictionary`.

---

## 6. Source generators — reflection moved to compile time

A source generator is a Roslyn component that runs **during compilation**, sees your syntax and
symbols, and adds new C# files to it. It cannot modify your code; it can only add.

You are already using several:

| Generator | Replaces |
|---|---|
| `[GeneratedRegex]` | run-time regex parsing and compilation |
| `System.Text.Json` source generation | reflection-based serialization |
| `[LoggerMessage]` | boxing and template parsing on every log call |
| `record` synthesis | hand-written `Equals`, `GetHashCode`, `ToString` |
| ASP.NET Core minimal API generation | run-time parameter-binding reflection |

📂 [`Domain/ValueObjects/Sku.cs`](../../src/LogiFlow.Domain/ValueObjects/Sku.cs) uses
`[GeneratedRegex]`. Note what it buys beyond speed: **an invalid pattern is a compile error**
rather than a `RegexParseException` on the unlucky code path.

The general trade is always the same, and it is a good one:

> **Do the work at compile time. It is faster, it is debuggable, it survives trimming and AOT,
> and mistakes become build errors instead of production ones.**

**Analyzers** are the same machinery pointed at correctness rather than code generation. This
repository treats warnings as errors in `src/` (module 00), which turns an analyzer from advice
into a rule.

---

## 7. `unsafe`, `fixed`, and native interop — the two-paragraph version

You will not write these in a business application, but you should be able to say what they are.

`unsafe` enables pointers. `fixed` **pins** an object so the GC cannot move it during compaction —
which is exactly why pinning for a long time fragments the heap, and why `Span<T>` exists as the
safe alternative for almost every case that used to need it.

`[LibraryImport]` (the source-generated successor to `[DllImport]`) calls native code. The costs
are the **marshalling** of arguments across the boundary and the fact that a native call blocks a
real thread. In this stack you meet it through libraries, not directly.

---

## 8. Do this

```bash
cd labs/Labs.Playground
dotnet run generics
dotnet run reflection
```

Then look at what the compiler actually produced. There is no better hour to spend on C#:

1. Open <https://sharplab.io>, paste a `record`, and read the generated `Equals`,
   `GetHashCode`, `<Clone>$` and `PrintMembers`. Every rule in module 01 is visible there.
2. Paste an `async` method and switch the output to **IL** or **C#** — the state machine struct,
   the `MoveNext` switch, and the awaiter fields are all right there. Module 04 stops being
   abstract.
3. Paste a `foreach` over a `List<T>` and then over an `IEnumerable<T>`, and compare: one uses the
   struct enumerator with no allocation, the other allocates an interface-typed one.
4. Paste a lambda that captures a loop variable and find the compiler-generated closure class.
   The `for`-loop trap in module 02 becomes a field on a display class you can see.

---

## 9. Golden rules

1. **A .dll is IL plus metadata, not machine code.** That is why reflection, decompilers and
   source generators are possible, and why shipping a build is not obfuscation.
2. **The JIT compiles per method, on first call.** First-request latency and misleading console
   timings both come from this.
3. **Tier 0 gets you started, tier 1 gets you fast, OSR bridges a long-running loop.** You do not
   configure it; you just stop being surprised by it.
4. **ReadyToRun for faster startup with no loss of peak speed; Native AOT when you can give up
   runtime code generation.** No `Expression.Compile`, no `Reflection.Emit`, and reflection over
   trimmed types throws.
5. **Every object carries a sync-block index and a method-table pointer.** That is `GetType()`,
   virtual dispatch, `lock`, and boxing, all from one 16-byte header.
6. **Generics are instantiated at run time: shared code for reference types, specialised code per
   value type.** Type safety and no boxing, at the cost of JIT time per struct instantiation.
7. **A static field in a generic type is per closed type.** `Cache<int>` and `Cache<string>` are
   two caches — a feature, and a foot-gun if you did not intend it.
8. **A generic constraint removes the boxing an interface parameter forces.** Same method body,
   240,000 bytes versus zero.
9. **Reflection is slow because of the metadata lookup and the boxing, not because it is
   reflection.** Cache the `MemberInfo`, build a delegate, and the cost is gone.
10. **Prefer a source generator to run-time reflection.** Compile-time work is faster, debuggable,
    trimming-safe, and turns run-time failures into build errors.
11. **Pinning fragments the heap.** `fixed` stops the GC moving an object; `Span<T>` is the safe
    answer for nearly every case that used to need it.

---

## 10. Interview questions

**"What happens between writing C# and running it?"**
Roslyn compiles to IL and metadata in an assembly, running analyzers and source generators on the
way. At run time the CLR loads the assembly, builds method tables, and JIT-compiles each method on
first call — tier 0 first, tier 1 once it is hot.

**"Why is the first request to an endpoint slower?"**
JIT compilation of every method on that path, plus one-time initialisation like the DI graph and
EF's model building. ReadyToRun removes most of the JIT part by precompiling a baseline.

**"How are .NET generics different from Java's?"**
Java erases them, so `List<int>` does not exist at run time and everything boxes. The CLR keeps
generics in metadata and instantiates them at run time: value types get specialised code with no
boxing, reference types share one canonical body, and `typeof(List<int>)` is a real type.

**"Why can a static abstract interface member not appear in an expression tree?"**
Because it is resolved per closed generic type at JIT time, while an expression tree is built at
compile time — there is no single method handle to embed. You build that node by hand with
`Expression.New` or similar.

**"Is reflection slow?"**
The metadata lookup is, and the boxing of value types is. The dispatch itself is not especially.
Cache the `MemberInfo` and build a delegate and it approaches a direct call — which is exactly
what serializers and ORMs do.

**"What is a source generator, and why prefer one?"**
A Roslyn component that adds C# to the compilation. It moves work that would otherwise be
reflection to compile time: faster, debuggable, trimming- and AOT-safe, and errors surface as
build failures. `[GeneratedRegex]`, `[LoggerMessage]` and JSON source generation are the ones you
will meet.

**"What are the trade-offs of Native AOT?"**
Startup in milliseconds, much lower memory, a self-contained binary; in exchange, no runtime code
generation at all and reflection limited to what the trimmer can prove is used. Great for a small
API or CLI, still awkward for EF Core.

---

## Next

→ [Module 23 — Text, culture, time and serialization](../module-23-text-culture-serialization/)
