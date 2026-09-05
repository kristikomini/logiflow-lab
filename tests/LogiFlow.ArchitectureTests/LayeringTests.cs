using System.Reflection;
using LogiFlow.Application.Abstractions.Messaging;
using LogiFlow.Domain.Common;
using NetArchTest.Rules;
using static LogiFlow.ArchitectureTests.ArchTestMessages;

namespace LogiFlow.ArchitectureTests;

/// <summary>
/// Tests over the dependency graph rather than over behaviour.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why these exist.</b> Clean Architecture's rules are conventions, and conventions decay.
/// Someone in a hurry adds <c>using Microsoft.EntityFrameworkCore;</c> to a domain class to
/// "just add a quick query", the reviewer is busy, and it merges. Six months later the Domain
/// cannot be tested without a database and nobody can point at when it happened.
/// </para>
/// <para>
/// An architecture test turns the convention into a build failure. It is the cheapest possible
/// insurance: a handful of tests that keep a codebase honest for years, and they run in
/// milliseconds because they only read metadata.
/// </para>
/// <para>
/// Covered in: <c>course/module-12-testing/05-architecture-tests.md</c>
/// </para>
/// </remarks>
public sealed class LayeringTests
{
    private static readonly Assembly DomainAssembly = typeof(Entity<>).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(IDispatcher).Assembly;
    private static readonly Assembly InfrastructureAssembly =
        typeof(LogiFlow.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly ApiAssembly = typeof(Program).Assembly;

    /// <summary>
    /// The most important test in the repository.
    /// </summary>
    /// <remarks>
    /// The Domain must depend on nothing but the base class library. If this fails, the
    /// dependency arrow has been reversed and the architecture is no longer Clean — it is just
    /// a folder structure.
    /// </remarks>
    [Fact]
    public void Domain_DependsOnNothingButTheBcl()
    {
        string[] forbidden =
        [
            "LogiFlow.Application",
            "LogiFlow.Infrastructure",
            "LogiFlow.Api",
            "Microsoft.EntityFrameworkCore",
            "Microsoft.AspNetCore",
            "Microsoft.Extensions.DependencyInjection",
            "FluentValidation",
            "Serilog",
            "Newtonsoft.Json",
        ];

        TestResult result = Types.InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOnAny(forbidden)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailureMessage(result, "Domain must not depend on outer layers or frameworks"));
    }

    /// <summary>The Application layer orchestrates; it must not know how anything is stored.</summary>
    [Fact]
    public void Application_DoesNotDependOnInfrastructureOrEfCore()
    {
        string[] forbidden =
        [
            "LogiFlow.Infrastructure",
            "LogiFlow.Api",
            "Microsoft.EntityFrameworkCore",
            "Microsoft.Data.SqlClient",
        ];

        TestResult result = Types.InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOnAny(forbidden)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            FailureMessage(result, "Application must talk to abstractions, never to EF Core directly"));
    }

    /// <summary>Nothing may depend on the composition root.</summary>
    [Fact]
    public void Infrastructure_DoesNotDependOnTheApi()
    {
        TestResult result = Types.InAssembly(InfrastructureAssembly)
            .Should()
            .NotHaveDependencyOn("LogiFlow.Api")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailureMessage(result, "Infrastructure must not reference the API"));
    }

    /// <summary>
    /// The API must not reach past the Application layer into the Domain's internals.
    /// </summary>
    /// <remarks>
    /// This one is deliberately <i>not</i> a blanket ban. Endpoints legitimately reference
    /// <c>Result</c>, <c>Error</c> and enums such as <c>OrderStatus</c> when shaping responses.
    /// What they must never do is load an aggregate and call business methods on it, bypassing
    /// the handler pipeline — so the rule targets the aggregates themselves.
    /// </remarks>
    [Fact]
    public void Api_DoesNotUseDomainAggregatesDirectly()
    {
        string[] aggregateNames =
        [
            "LogiFlow.Domain.Orders.Order",
            "LogiFlow.Domain.Catalog.Product",
            "LogiFlow.Domain.Customers.Customer",
            "LogiFlow.Domain.Inventory.Warehouse",
            "LogiFlow.Domain.Shipping.Shipment",
        ];

        TestResult result = Types.InAssembly(ApiAssembly)
            .That().ResideInNamespace("LogiFlow.Api.Endpoints")
            .Should()
            .NotHaveDependencyOnAny(aggregateNames)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            FailureMessage(result, "Endpoints must go through the dispatcher, not manipulate aggregates"));
    }
}

/// <summary>Conventions that keep the codebase internally consistent.</summary>
public sealed class ConventionTests
{
    private static readonly Assembly ApplicationAssembly = typeof(IDispatcher).Assembly;
    private static readonly Assembly DomainAssembly = typeof(Entity<>).Assembly;

    /// <summary>
    /// Handlers must be internal so nothing can call one directly.
    /// </summary>
    /// <remarks>
    /// A public handler is an invitation to inject <c>SubmitOrderCommandHandler</c> and call
    /// <c>HandleAsync</c> straight from an endpoint — skipping validation, logging and the
    /// transaction. Keeping them internal makes the dispatcher the only route in.
    /// </remarks>
    [Fact]
    public void RequestHandlers_AreInternalAndSealed()
    {
        List<Type> handlers = [.. ApplicationAssembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => t.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)))];

        handlers.ShouldNotBeEmpty("the scan found no handlers at all - the test itself is broken");

        List<string> offenders = [.. handlers
            .Where(t => t.IsPublic || !t.IsSealed)
            .Select(t => $"{t.Name} (public={t.IsPublic}, sealed={t.IsSealed})")];

        offenders.ShouldBeEmpty(
            "handlers must be internal (so only the dispatcher reaches them) and sealed "
            + $"(so the JIT can devirtualize): {string.Join(", ", offenders)}");
    }

    /// <summary>Every aggregate must be sealed — inheritance between aggregates is never right.</summary>
    [Fact]
    public void Aggregates_AreSealed()
    {
        List<string> offenders = [.. DomainAssembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(IsAggregateRoot)
            .Where(t => !t.IsSealed)
            .Select(t => t.Name)];

        offenders.ShouldBeEmpty($"these aggregates should be sealed: {string.Join(", ", offenders)}");
    }

    /// <summary>
    /// Domain events must be immutable records named in the past tense.
    /// </summary>
    /// <remarks>
    /// The naming rule is not pedantry. <c>OrderSubmittedDomainEvent</c> is a fact;
    /// <c>SubmitOrderEvent</c> reads like a command, and teams that blur the two end up writing
    /// handlers that try to reject events — which is meaningless, because the thing already
    /// happened.
    /// </remarks>
    [Fact]
    public void DomainEvents_AreRecordsNamedInThePastTense()
    {
        List<Type> events = [.. DomainAssembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => typeof(IDomainEvent).IsAssignableFrom(t))];

        events.ShouldNotBeEmpty("the scan found no domain events - the test itself is broken");

        List<string> badlyNamed = [.. events
            .Where(t => !t.Name.EndsWith("DomainEvent", StringComparison.Ordinal))
            .Select(t => t.Name)];

        badlyNamed.ShouldBeEmpty($"domain events must end in 'DomainEvent': {string.Join(", ", badlyNamed)}");

        // A record generates a compiler-synthesised <Clone>$ method; a plain class does not.
        // This is the standard reflection trick for "is this type a record?".
        List<string> notRecords = [.. events
            .Where(t => t.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.Instance) is null)
            .Select(t => t.Name)];

        notRecords.ShouldBeEmpty(
            $"domain events must be records so they are immutable: {string.Join(", ", notRecords)}");
    }

    /// <summary>Commands and queries must be records — a mutable message is a bug waiting to happen.</summary>
    [Fact]
    public void CommandsAndQueries_AreRecords()
    {
        List<Type> messages = [.. ApplicationAssembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => t.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>)))];

        messages.ShouldNotBeEmpty("the scan found no commands or queries - the test itself is broken");

        List<string> notRecords = [.. messages
            .Where(t => t.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.Instance) is null)
            .Select(t => t.Name)];

        notRecords.ShouldBeEmpty(
            "commands and queries must be records - a behaviour that mutated a shared request "
            + $"would corrupt every later step in the pipeline: {string.Join(", ", notRecords)}");
    }

    private static bool IsAggregateRoot(Type type)
    {
        for (Type? current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(AggregateRoot<>))
            {
                return true;
            }
        }

        return false;
    }
}
