using System.Linq.Expressions;
using System.Reflection;
using LogiFlow.Domain.Common;
using LogiFlow.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace LogiFlow.Infrastructure.Persistence.Conventions;

/// <summary>
/// Teaches EF Core to store every <see cref="IStronglyTypedId{TSelf}"/> as a plain
/// <c>uniqueidentifier</c>, without a hand-written converter per type.
/// </summary>
/// <remarks>
/// <para>
/// <b>The problem.</b> EF Core has no idea what an <c>OrderId</c> is. Left alone it either
/// throws "property could not be mapped" or tries to map it as a complex type with its own
/// table. It needs a <see cref="ValueConverter"/> saying "to store this, take <c>.Value</c>;
/// to read it, rebuild it from the Guid".
/// </para>
/// <para>
/// <b>The naive fix</b> is one line and one converter class per ID type — seven identifiers,
/// fourteen things to forget. What happens when you forget one is instructive: nothing, until
/// runtime, when EF throws a model-validation error naming a property you have not thought
/// about in months.
/// </para>
/// <para>
/// This convention finds every ID type by reflection and registers a converter for each, so
/// adding an eighth identifier requires no persistence change at all.
/// </para>
/// Covered in: <c>course/module-06-efcore/02-value-conversions.md</c>
/// </remarks>
public static class StronglyTypedIdConvention
{
    /// <summary>Registers a converter for every strongly-typed id found in the Domain assembly.</summary>
    public static void RegisterStronglyTypedIds(this ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        // Anchored on a type that genuinely lives in the Domain assembly. Assembly.Load("...")
        // would be a string the compiler cannot check and a runtime failure the day someone
        // renames the project.
        IEnumerable<Type> idTypes = typeof(OrderId).Assembly
            .GetTypes()
            .Where(t => t is { IsValueType: true, IsGenericTypeDefinition: false })
            .Where(t => t.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IStronglyTypedId<>)));

        foreach (Type idType in idTypes)
        {
            Type converterType = typeof(StronglyTypedIdConverter<>).MakeGenericType(idType);

            configurationBuilder
                .Properties(idType)
                .HaveConversion(converterType);
        }
    }
}

/// <summary>
/// Converts one strongly-typed id to and from the <see cref="Guid"/> stored in the database.
/// </summary>
/// <typeparam name="TId">The identifier type.</typeparam>
/// <remarks>
/// <para>
/// <b>Why the expression trees are hand-built instead of written as lambdas.</b> The obvious
/// implementation is:
/// </para>
/// <code>
/// : base(id =&gt; id.Value, value =&gt; TId.From(value))
/// </code>
/// <para>
/// That does not compile. It produces <b>CS8927: an expression tree may not contain an access
/// of static virtual or abstract interface member</b>. Static abstract members
/// (<c>TId.From</c>) are resolved by the JIT per closed generic type, and an expression tree is
/// built at compile time — there is no single method handle to put in the tree.
/// </para>
/// <para>
/// So the trees are constructed by hand with <see cref="Expression"/>, targeting the concrete
/// type's own constructor and property. The result is identical to what the compiler would emit
/// for a non-generic converter, and EF inlines both into the materialisation code it compiles
/// per entity type — no per-row reflection, no delegate invocation.
/// </para>
/// <para>
/// This is a genuinely common wall to hit when combining generic math or static abstracts with
/// anything expression-tree-based (EF Core, LINQ providers, mocking libraries). The fix is
/// always the same: drop to <see cref="Expression"/> and build the node yourself.
/// </para>
/// </remarks>
public sealed class StronglyTypedIdConverter<TId> : ValueConverter<TId, Guid>
    where TId : struct, IStronglyTypedId<TId>
{
    /// <summary>Creates the converter.</summary>
    public StronglyTypedIdConverter()
        : base(ToProvider(), FromProvider())
    {
    }

    /// <summary>Builds <c>id =&gt; id.Value</c> against the concrete type's own property.</summary>
    private static Expression<Func<TId, Guid>> ToProvider()
    {
        ParameterExpression id = Expression.Parameter(typeof(TId), "id");

        PropertyInfo valueProperty = typeof(TId).GetProperty(nameof(IStronglyTypedId<TId>.Value))
            ?? throw new InvalidOperationException(
                $"'{typeof(TId).Name}' has no public 'Value' property; it cannot be a strongly-typed id.");

        // Resolving the property on the CONCRETE type rather than the interface avoids an
        // interface dispatch that would box the struct on every single read.
        return Expression.Lambda<Func<TId, Guid>>(Expression.Property(id, valueProperty), id);
    }

    /// <summary>Builds <c>value =&gt; new TId(value)</c> against the concrete type's constructor.</summary>
    private static Expression<Func<Guid, TId>> FromProvider()
    {
        ParameterExpression value = Expression.Parameter(typeof(Guid), "value");

        // Every id is declared `readonly record struct X(Guid Value)`, so the positional
        // parameter gives it a public constructor taking a single Guid.
        ConstructorInfo constructor = typeof(TId).GetConstructor([typeof(Guid)])
            ?? throw new InvalidOperationException(
                $"'{typeof(TId).Name}' has no constructor taking a single Guid. Strongly-typed ids "
                + "must be declared as `readonly record struct X(Guid Value)`.");

        return Expression.Lambda<Func<Guid, TId>>(Expression.New(constructor, value), value);
    }
}
