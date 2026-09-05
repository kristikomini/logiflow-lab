# 5. Architecture tests

> Part of [Module 12 — Testing](README.md), section 5.
> Previous: [4. Integration tests](03-integration-testing.md) ·
> Next: [6. What not to test](README.md#6-what-not-to-test)

---

Every architectural rule in this repository — dependencies point inward, the domain references
nothing, aggregates are sealed — is a **convention**. And conventions decay.

Not dramatically. Someone in a hurry adds `using Microsoft.EntityFrameworkCore;` to a domain class to
"just add a quick query", the reviewer is busy, and it merges. Six months later the Domain project
cannot be tested without a database and nobody can point at when that happened.

An architecture test turns the convention into a **build failure**. It is the cheapest insurance in
the repository: a handful of tests that keep a codebase honest for years, running in milliseconds
because they only read assembly metadata.

## The most important test here

```csharp
private static readonly Assembly DomainAssembly = typeof(Entity<>).Assembly;
private static readonly Assembly ApplicationAssembly = typeof(IDispatcher).Assembly;
private static readonly Assembly InfrastructureAssembly = typeof(LogiFlow.Infrastructure.DependencyInjection).Assembly;

[Fact]
public void Domain_depends_on_nothing()
{
    TestResult result = Types.InAssembly(DomainAssembly)
        .ShouldNot()
        .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "LogiFlow.Application", "LogiFlow.Infrastructure")
        .GetResult();

    result.IsSuccessful.ShouldBeTrue(FailureMessage(result));
}
```

If this fails, the domain is no longer the domain. Everything else in
[module 05](../module-05-clean-architecture/) rests on it: the domain tests run in milliseconds
*because* there is nothing to construct, and `Order` can be reasoned about without knowing a database
exists.

**Assemblies are identified by a type, not by a string.** `typeof(Entity<>).Assembly` survives a
project rename; `"LogiFlow.Domain"` does not, and a renamed project silently makes the test pass
against nothing.

## The rest of the layering

```csharp
[Fact]
public void Application_does_not_depend_on_Infrastructure() =>
    Types.InAssembly(ApplicationAssembly)
        .ShouldNot().HaveDependencyOn("LogiFlow.Infrastructure")
        .GetResult().IsSuccessful.ShouldBeTrue();
```

This is the one that proves
[dependency inversion](../module-05-clean-architecture/README.md#1-one-rule-dependencies-point-inward)
is real rather than aspirational. `IOrderRepository` lives in Application and `OrderRepository` in
Infrastructure, so the arrow between the projects runs Infrastructure → Application. The moment
somebody adds a project reference "just to use the DbContext directly", the build goes red.

The project reference alone would usually prevent it — but not always: a shared package, a `dynamic`
call, or reflection can reintroduce the coupling in ways the compiler permits.

## Convention tests worth having

Layering is the start. The pattern generalises to anything expressible over types:

```csharp
[Fact]
public void Handlers_are_sealed() =>
    Types.InAssembly(ApplicationAssembly)
        .That().ImplementInterface(typeof(IRequestHandler<,>))
        .Should().BeSealed()
        .GetResult().IsSuccessful.ShouldBeTrue();

[Fact]
public void Aggregate_roots_have_a_private_parameterless_constructor() => …   // EF materialisation

[Fact]
public void Domain_events_are_records() => …                                  // facts are immutable

[Fact]
public void Nothing_outside_Infrastructure_references_DbContext() => …
```

Each replaces a line in a code-review checklist that a human would otherwise have to remember — and
that a new joiner has never read.

**`FailureMessage(result)`** is worth the ten lines it costs. The default assertion says "expected
true, was false", which tells you a rule broke and not which type broke it. Listing
`result.FailingTypeNames` turns the failure into a fix.

## What they cannot do

Be honest about the limits; this is where the interview answer gets interesting.

**They check types and references, not behaviour.** Nothing here stops a handler from being a
thousand lines, or a domain method from being wrong.

**They can be too strict.** A rule that fires on every legitimate exception gets an ever-growing
allow-list, and then somebody deletes it. Write rules you believe in absolutely; leave the
"usually" ones to review.

**They only run if somebody runs them.** They must be in the CI test run, not a project people build
locally and forget.

**Naming conventions are the weakest kind.** "All handlers end in `Handler`" catches a typo and
protects nothing structural. Prefer rules about *dependencies*, which is where the real decay
happens.

## Where else this idea appears

The same instinct — encode the rule so the build enforces it — shows up throughout:

- `Directory.Build.props` with `TreatWarningsAsErrors` and analyser rules like CA2254 for
  [message templates](../module-11-observability/02-structured-logging.md).
- A `DbCommandInterceptor` asserting query counts to catch an
  [N+1](../module-06-efcore/04-n-plus-one.md) in a test.
- `Directory.Packages.props` centralising versions so two projects cannot disagree.

Architecture tests are the version that works on your *own* rules rather than the compiler's, and
they cost about an hour to write once.

## Try it

Add this to any file in the Domain project:

```csharp
using Microsoft.EntityFrameworkCore;
```

Then:

```bash
dotnet test tests/LogiFlow.ArchitectureTests
```

It fails by name, in under a second, and tells you which type broke the rule. Now try the other
direction — add a reference from `LogiFlow.Application` to `LogiFlow.Infrastructure` and use
something from it — and watch a different test fail.

That is the difference between a convention written in a README and a rule the build enforces.

## What to remember

- Conventions decay silently. Architecture tests turn them into build failures.
- The most important one: the Domain depends on nothing.
- The second: Application does not depend on Infrastructure — that is DIP made checkable.
- Identify assemblies by a type, not by a string, so a rename cannot make the test vacuous.
- They read metadata, so they run in milliseconds.
- Report the failing type names, or a failure tells you nothing actionable.
- They check structure, not behaviour, and a rule with a growing allow-list should be deleted.
- Prefer dependency rules to naming rules.

**Code:** [`LayeringTests.cs`](../../tests/LogiFlow.ArchitectureTests/LayeringTests.cs) ·
[`FailureMessage.cs`](../../tests/LogiFlow.ArchitectureTests/FailureMessage.cs)

**Next:** [6. What not to test](README.md#6-what-not-to-test)
