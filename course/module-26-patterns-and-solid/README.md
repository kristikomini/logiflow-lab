# Module 26 — Design patterns and SOLID, in this codebase

> Patterns taught from a catalogue are how people learn to add a factory to everything. This
> module does the opposite: it points at the patterns already in this repository, says which
> problem each one was solving, and — the more useful half — names the ones that are *not* here
> and why. A senior candidate can argue both sides of every pattern in this list.

---

## 1. SOLID, with the version that actually helps

Everyone can recite the letters. The distinguishing answer is knowing what each one *buys*, and
when it does not apply.

**S — Single responsibility.** *"A class should have one reason to change."* Not "one method",
and not "small". The test is the **reason**: if two different stakeholders can each demand a
change to the same class, it has two responsibilities.
📂 [`CreateOrder.cs`](../../src/LogiFlow.Application/Features/Orders/CreateOrder.cs) — the handler
orchestrates; the validator validates; the domain decides. Three reasons to change, three files.

**O — Open for extension, closed for modification.** Add behaviour without editing existing code.
📂 [`Application/Behaviors/`](../../src/LogiFlow.Application/Behaviors/) is the clean example:
adding caching meant adding `CachingBehavior`, not editing the dispatcher.
*When it does not apply:* speculative extensibility. An interface with one implementation, added
"in case", is cost with no benefit. Wait for the second implementation.

**L — Liskov substitution.** A subtype must be usable wherever the base is, **without the caller
knowing**. The classic violation is a derived class that throws `NotSupportedException` from an
inherited member, or strengthens a precondition. This is also why records include the
`EqualityContract` check (module 01) and why `sealed` is a good default: a class not designed for
inheritance is a Liskov violation waiting to be written.

**I — Interface segregation.** Small, role-shaped interfaces. 📂 [`IRepositories.cs`](../../src/LogiFlow.Application/Abstractions/Data/IRepositories.cs)
declares what the *application* needs, not everything the database can do. A `IRepository<T>` with
sixteen members forces every implementation and every test double to deal with fifteen it does not
use.

**D — Dependency inversion.** High-level policy must not depend on low-level detail; both depend
on an abstraction — **and the abstraction is owned by the high-level module.** That last clause is
the whole idea, and it is the one people omit. `IOrderRepository` lives in the Application layer,
not in Infrastructure. That single placement decision is what makes the dependency arrow point
inward (module 05); putting the interface next to its implementation is dependency inversion in
name only.

---

## 2. The patterns this repository actually uses

| Pattern | Where | The problem it solved |
|---|---|---|
| **Mediator** | 📂 [`Dispatcher.cs`](../../src/LogiFlow.Application/Abstractions/Messaging/Dispatcher.cs) | one entry point per use case, and one place to wrap them all |
| **Decorator (pipeline)** | 📂 [`Behaviors/`](../../src/LogiFlow.Application/Behaviors/) | validation, logging, caching, transactions — without touching a handler |
| **Repository + Unit of Work** | 📂 [`IRepositories.cs`](../../src/LogiFlow.Application/Abstractions/Data/IRepositories.cs), [`IUnitOfWork.cs`](../../src/LogiFlow.Application/Abstractions/Data/IUnitOfWork.cs) | keep EF Core out of the Application layer's vocabulary |
| **Specification** | 📂 [`Specification.cs`](../../src/LogiFlow.Domain/Common/Specifications/Specification.cs) | a named, composable, *translatable* business rule |
| **Strategy** | 📂 [`CustomerTier.cs`](../../src/LogiFlow.Domain/Customers/CustomerTier.cs) | a discount per tier, chosen by data rather than by an `if` |
| **Factory method** | 📂 [`Order.Create`](../../src/LogiFlow.Domain/Orders/Order.cs), `Sku.Create` | an invalid object cannot be constructed at all |
| **Value object** | 📂 [`Money.cs`](../../src/LogiFlow.Domain/ValueObjects/Money.cs), `Address`, `Sku` | make illegal states unrepresentable |
| **Domain events / Observer** | 📂 [`IDomainEvent.cs`](../../src/LogiFlow.Domain/Common/IDomainEvent.cs) | side effects without the aggregate knowing who cares |
| **Options** | 📂 `JwtOptions` | configuration as a typed, validated object |
| **Result** | 📂 [`Result.cs`](../../src/LogiFlow.Domain/Results/Result.cs) | expected failures as values, not exceptions |
| **Outbox** | 📂 [`OutboxMessage.cs`](../../src/LogiFlow.Infrastructure/Persistence/Outbox/OutboxMessage.cs) | the dual-write problem (module 25) |
| **Adapter** | 📂 [`Infrastructure/Services/`](../../src/LogiFlow.Infrastructure/Services/) | the outside world, behind an interface we own |

**Two of those are worth arguing about, because an interviewer will.**

**Repository over an ORM.** The honest case against: `DbSet<T>` *is* a repository and
`DbContext` *is* a unit of work, so the pattern can be a layer of indirection that adds nothing —
and a `IRepository<T>` exposing `IQueryable<T>` leaks EF straight through the abstraction it was
meant to provide. The case for, as used here: the Application layer states its needs in domain
language (`GetWithLinesAsync`), tests do not need a database, and swapping Dapper in for a hot
query changes one file. **The rule that makes it worth having: never expose `IQueryable` from a
repository.** The moment you do, the abstraction is decorative.

**Mediator.** The case against: it turns a compile-time call into a run-time lookup, so
"find all callers" stops working and a missing registration is a run-time failure. The case for:
one place to add a cross-cutting concern for *every* use case, and endpoints that depend on one
interface rather than on twenty handlers. 📂 The dispatcher here is ~60 lines and hand-written —
module 08 builds it — which is deliberate: this is not magic, and you should be able to explain
every line of it.

---

## 3. The patterns deliberately not here

Being able to say *why not* is worth more than the catalogue.

**Singleton (the GoF one, with the static `Instance` property).** Superseded by the DI container's
singleton lifetime, which gives you the same single instance without the static, and lets you
substitute it in a test. The GoF version is a global variable with a design-pattern name.

**Service Locator.** Injecting `IServiceProvider` and resolving from it hides dependencies from the
constructor, so the class no longer declares what it needs and the compiler cannot help. It is
widely considered an anti-pattern for that reason. Exception: a genuine factory that resolves by
run-time key — which is what `IServiceScopeFactory` in the outbox processor is doing, and why
that use is fine.

**Abstract Factory / Builder for domain objects.** A static factory method (`Order.Create`) is
simpler and reads better. Builders earn their place in test fixtures, where they genuinely reduce
noise.

**A generic `IRepository<T>` base class with sixteen methods.** Interface segregation says no.
Most aggregates need three operations, and they are not the same three.

**AutoMapper, or any convention-based mapper, across a boundary.** Mapping is explicit here on
purpose: a renamed property should be a *compile* error, not a null at run time. The cost is a few
lines of assignment; the benefit is that the compiler checks your contract.

**The anti-patterns worth naming out loud:**

- **Anemic domain model** — entities that are property bags, with all the behaviour in "services".
  This is the default shape of most enterprise code, and module 05 is the argument against it.
- **God repository / God service** — everything for one aggregate in one 900-line class.
- **Primitive obsession** — `string sku`, `decimal amount`. Two `string` parameters in a row are
  a bug waiting for someone to swap them; `Sku` and `Money` cannot be swapped.
- **Stringly-typed everything** — magic strings for policy names, claim types and configuration
  keys. `nameof` and a constant, always.
- **Leaky abstraction** — an interface whose signature reveals its implementation. `IQueryable`
  from a repository; `Task<DataTable>` from a service; a `[JsonPropertyName]` on a domain entity.

---

## 4. When a pattern is the wrong answer

The failure mode of learning patterns is applying them. Three questions before you reach for one:

1. **What would the code look like without it?** If that version is clear, write it. Two `if`
   statements beat a strategy hierarchy.
2. **Is the flexibility needed now, or imagined?** YAGNI is not laziness; every abstraction has a
   permanent cost in reading time paid by everyone who comes after.
3. **Does it hide something the reader needs to see?** A pattern that makes control flow harder to
   follow has to buy something real.

The useful heuristic: **the second occurrence is a coincidence, the third is a pattern.** Duplicate
once; abstract on the third. The opposite mistake — abstracting on the first — produces the wrong
abstraction, and a wrong abstraction is much more expensive than duplication because everything
gets built on top of it.

---

## 5. Do this

1. **Justify one pattern in this repo out loud, both ways.** Pick `Specification`. Argue that it is
   valuable indirection, then argue it is a wrapper around `Expression<Func<T,bool>>` that a
   private method would have done. Decide which you believe. That is the interview question.

2. **Find the anemic parts.** Look for a place where a handler reaches into an entity's properties
   to compute something the entity should compute. Move it, and watch the handler shrink.

3. **Add a behaviour.** Write a `RetryBehavior<TRequest,TResponse>` in `Application/Behaviors/`,
   register it, and add it to the pipeline. You will have used the decorator pattern, the
   open/closed principle and dependency inversion in one file — and changed no existing handler,
   which is the entire point.

4. **Remove one.** Take the smallest use case and call its handler directly from the endpoint,
   without the dispatcher. Note exactly what you lose. Now you can argue the mediator honestly.

---

## 6. Golden rules

1. **Single responsibility is about REASONS TO CHANGE, not size.** Two stakeholders who can each
   demand a change to one class means two responsibilities.
2. **Dependency inversion means the high-level module owns the abstraction.** `IOrderRepository`
   lives in Application, not next to its implementation — that placement is what makes the
   dependency arrow point inward.
3. **A subtype must be substitutable without the caller knowing.** A derived member that throws
   `NotSupportedException` is a Liskov violation, and `sealed` is a good default because of it.
4. **Never expose `IQueryable` from a repository.** The moment you do, the abstraction is
   decorative and EF Core has leaked into the layer that was supposed to be free of it.
5. **A pattern is an answer to a problem you can state.** If you cannot say what would go wrong
   without it, you do not need it yet.
6. **Duplicate twice; abstract on the third.** A wrong abstraction costs far more than duplication,
   because everything else gets built on top of it.
7. **Prefer a static factory method to a factory class**, and let it return a `Result` so an
   invalid object cannot be constructed at all.
8. **Injecting `IServiceProvider` hides your dependencies.** Constructor parameters are a public
   declaration of what a class needs; resolving from the container is a secret.
9. **Map explicitly across a published boundary.** A renamed property should be a compile error,
   not a null at run time.
10. **Primitive obsession is a type-system problem with a simple fix.** Two adjacent `string`
    parameters can be swapped silently; `Sku` and `CustomerName` cannot.
11. **The GoF singleton is a global variable.** Use the container's singleton lifetime, which is
    substitutable in a test.
12. **An anemic domain model is the default, not a design.** Behaviour belongs next to the data it
    protects.

---

## 7. Interview questions

**"Explain SOLID with an example from your own code."**
Take one letter and go deep rather than listing five shallowly. Dependency inversion is the best
choice, because the interesting half — *who owns the interface* — is the half most candidates
leave out. Show that `IOrderRepository` lives in the Application layer and say what that buys.

**"Is the repository pattern still useful with EF Core?"**
It is genuinely contested. `DbSet` is already a repository and `DbContext` is already a unit of
work, so a thin wrapper can be pure indirection — and one exposing `IQueryable` is worse than
none, because it leaks EF while claiming not to. It earns its place when the Application layer
speaks in domain language, when tests need no database, and when you may want Dapper for a hot
query. State the trade-off; do not recite the pattern.

**"What is the difference between a decorator and a mediator pipeline behaviour?"**
None, structurally — a behaviour *is* a decorator applied by convention rather than by hand. The
pipeline just makes the composition data-driven, so adding a cross-cutting concern touches one
registration instead of every handler.

**"When would you NOT use a design pattern?"**
When you cannot state the problem it solves in your own code today. Speculative extensibility —
an interface with one implementation, a factory for one type — costs every future reader and buys
nothing until the second implementation exists.

**"What is an anemic domain model, and is it always wrong?"**
Entities reduced to property bags with the behaviour in service classes. It is wrong when the
domain has real invariants, because nothing protects them and the same rule gets re-implemented in
three services. It is fine for genuine CRUD — a lookup table does not need a domain model, and
insisting otherwise is its own anti-pattern.

**"How do you decide between an abstraction and duplication?"**
Duplicate until the third occurrence, because two occurrences do not yet show you the shape of the
variation. A premature abstraction is harder to remove than duplication is to consolidate — you
end up adding parameters to it, which is how you get a method with a boolean flag.

---

## Next

→ [Module 27 — C# version by version](../module-27-csharp-versions/)
