using System.Linq.Expressions;

namespace LogiFlow.Domain.Common.Specifications;

/// <summary>
/// A reusable, composable, named query rule.
/// </summary>
/// <remarks>
/// <para>
/// <b>The problem it solves.</b> Repository interfaces rot into this:
/// </para>
/// <code>
/// Task&lt;List&lt;Order&gt;&gt; GetByCustomerAsync(CustomerId id);
/// Task&lt;List&lt;Order&gt;&gt; GetByCustomerAndStatusAsync(CustomerId id, OrderStatus status);
/// Task&lt;List&lt;Order&gt;&gt; GetByCustomerAndStatusAndDateRangeAsync(...);   // and so on, forever
/// </code>
/// <para>
/// Every new combination of filters is a new method. Twenty methods later nobody can find the
/// one they need, so they add a twenty-first.
/// </para>
/// <para>
/// A specification makes the <i>criteria</i> the parameter:
/// <c>Task&lt;List&lt;Order&gt;&gt; ListAsync(Specification&lt;Order&gt; spec)</c>. One method,
/// unlimited combinations, and each rule gets a business name — <c>OverdueOrdersSpec</c> reads
/// better in code review than an inline lambda comparing timestamps.
/// </para>
/// <para>
/// <b>The two evaluation modes are the point.</b> <see cref="ToExpression"/> hands EF Core a
/// tree it can translate to SQL. <see cref="IsSatisfiedBy"/> compiles the same rule and runs it
/// in memory — so a unit test can verify the business rule with a plain object and no database
/// at all. One definition, guaranteed to agree in both places.
/// </para>
/// <para>
/// <b>Know the counter-argument.</b> Specifications add a layer, and for a codebase with a
/// handful of simple queries they are overhead. They earn their keep when the same rule appears
/// in several places, or when rules compose combinatorially. Recognising which situation you are
/// in is the actual judgement.
/// </para>
/// Covered in: <c>course/module-06-efcore/06-specification-pattern.md</c>
/// </remarks>
/// <typeparam name="T">The entity being filtered.</typeparam>
public abstract class Specification<T>
{
    /// <summary>The rule, as a tree EF Core can turn into SQL.</summary>
    public abstract Expression<Func<T, bool>> ToExpression();

    /// <summary>
    /// Evaluates the rule against an in-memory instance.
    /// </summary>
    /// <remarks>
    /// Compiles the expression on every call, which is fine for a test or a one-off check and
    /// wasteful in a loop over thousands of items. Cache <c>ToExpression().Compile()</c> yourself
    /// if you need that; the cost is measured in <c>Labs.Benchmarks</c>.
    /// </remarks>
    public bool IsSatisfiedBy(T entity) => ToExpression().Compile()(entity);

    /// <summary>Both rules must hold.</summary>
    public Specification<T> And(Specification<T> other) => new AndSpecification<T>(this, other);

    /// <summary>Either rule may hold.</summary>
    public Specification<T> Or(Specification<T> other) => new OrSpecification<T>(this, other);

    /// <summary>The rule must not hold.</summary>
    public Specification<T> Not() => new NotSpecification<T>(this);

    /// <summary>Lets a specification be passed anywhere an expression is expected.</summary>
    /// <remarks>
    /// This is what allows <c>query.Where(spec)</c> to just work, instead of the noisier
    /// <c>query.Where(spec.ToExpression())</c> at every call site.
    /// </remarks>
    public static implicit operator Expression<Func<T, bool>>(Specification<T> specification) =>
        specification.ToExpression();
}

/// <summary>Conjunction of two specifications.</summary>
/// <typeparam name="T">The entity being filtered.</typeparam>
internal sealed class AndSpecification<T>(Specification<T> left, Specification<T> right) : Specification<T>
{
    public override Expression<Func<T, bool>> ToExpression() =>
        left.ToExpression().And(right.ToExpression());
}

/// <summary>Disjunction of two specifications.</summary>
/// <typeparam name="T">The entity being filtered.</typeparam>
internal sealed class OrSpecification<T>(Specification<T> left, Specification<T> right) : Specification<T>
{
    public override Expression<Func<T, bool>> ToExpression() =>
        left.ToExpression().Or(right.ToExpression());
}

/// <summary>Negation of a specification.</summary>
/// <typeparam name="T">The entity being filtered.</typeparam>
internal sealed class NotSpecification<T>(Specification<T> inner) : Specification<T>
{
    public override Expression<Func<T, bool>> ToExpression() => inner.ToExpression().Not();
}

/// <summary>
/// Wraps a raw lambda as a specification, for one-off rules that do not deserve their own class.
/// </summary>
/// <typeparam name="T">The entity being filtered.</typeparam>
/// <param name="predicate">The rule.</param>
public sealed class AdHocSpecification<T>(Expression<Func<T, bool>> predicate) : Specification<T>
{
    /// <inheritdoc />
    public override Expression<Func<T, bool>> ToExpression() => predicate;
}
