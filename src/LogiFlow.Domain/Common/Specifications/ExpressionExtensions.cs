using System.Linq.Expressions;

namespace LogiFlow.Domain.Common.Specifications;

/// <summary>
/// Combines <see cref="Expression{TDelegate}"/> predicates with And / Or / Not.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the file that teaches you what LINQ actually is.</b>
/// </para>
/// <para>
/// <c>Func&lt;Order, bool&gt;</c> is compiled code — a black box you can only invoke.
/// <c>Expression&lt;Func&lt;Order, bool&gt;&gt;</c> is a <i>data structure</i> describing that
/// code: a tree of nodes saying "compare the Status member to the constant Shipped". The C#
/// compiler builds it for you from the same lambda syntax.
/// </para>
/// <para>
/// That difference is the entire reason EF Core can turn <c>Where(o =&gt; o.Total &gt; 100)</c>
/// into <c>WHERE Total &gt; 100</c>. It walks the tree and emits SQL. Hand it a compiled
/// <c>Func</c> instead and it cannot see inside, so it must pull every row into memory and
/// filter there — the single most common cause of a .NET app that dies under real data volume.
/// </para>
/// <para>
/// <b>The naive combination attempt, and why it fails:</b>
/// </para>
/// <code>
/// // Compiles. Throws at query time.
/// Expression&lt;Func&lt;T, bool&gt;&gt; Bad(Expression&lt;Func&lt;T,bool&gt;&gt; a, Expression&lt;Func&lt;T,bool&gt;&gt; b)
///     =&gt; x =&gt; a.Compile()(x) &amp;&amp; b.Compile()(x);
/// </code>
/// <para>
/// EF Core reaches <c>a.Compile()</c>, finds an opaque delegate invocation it cannot translate,
/// and either throws or silently falls back to client evaluation.
/// </para>
/// <para>
/// The fix is to rewrite the second expression's tree so it uses the <i>first</i> expression's
/// parameter node, then join the two bodies with a single <c>AndAlso</c> node. That produces one
/// coherent tree EF can translate. <see cref="ParameterReplacer"/> below does the rewriting, via
/// <see cref="ExpressionVisitor"/> — the standard way to transform an expression tree.
/// </para>
/// Covered in: <c>course/module-03-linq-internals/03-expression-trees.md</c>
/// </remarks>
public static class ExpressionExtensions
{
    /// <summary>Produces a predicate matching items satisfying both inputs.</summary>
    public static Expression<Func<T, bool>> And<T>(
        this Expression<Func<T, bool>> left,
        Expression<Func<T, bool>> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        // Rewrite `right` so every reference to ITS parameter points at LEFT's parameter.
        // Without this the combined tree would contain two distinct parameter nodes and
        // LambdaExpression.Create would throw "variable 'x' of type 'T' referenced from
        // scope, but it is not defined".
        Expression rewrittenRight = new ParameterReplacer(right.Parameters[0], left.Parameters[0])
            .Visit(right.Body);

        return Expression.Lambda<Func<T, bool>>(
            Expression.AndAlso(left.Body, rewrittenRight),
            left.Parameters[0]);
    }

    /// <summary>Produces a predicate matching items satisfying either input.</summary>
    public static Expression<Func<T, bool>> Or<T>(
        this Expression<Func<T, bool>> left,
        Expression<Func<T, bool>> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        Expression rewrittenRight = new ParameterReplacer(right.Parameters[0], left.Parameters[0])
            .Visit(right.Body);

        return Expression.Lambda<Func<T, bool>>(
            Expression.OrElse(left.Body, rewrittenRight),
            left.Parameters[0]);
    }

    /// <summary>Produces a predicate matching items that do <i>not</i> satisfy the input.</summary>
    public static Expression<Func<T, bool>> Not<T>(this Expression<Func<T, bool>> expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        return Expression.Lambda<Func<T, bool>>(
            Expression.Not(expression.Body),
            expression.Parameters[0]);
    }

    /// <summary>A predicate matching everything. The identity element for <see cref="And{T}"/>.</summary>
    /// <remarks>
    /// Useful as a fold seed when building a filter from optional criteria:
    /// <code>
    /// var filter = ExpressionExtensions.True&lt;Order&gt;();
    /// if (status is not null) filter = filter.And(o =&gt; o.Status == status);
    /// if (from   is not null) filter = filter.And(o =&gt; o.CreatedAtUtc &gt;= from);
    /// </code>
    /// EF Core's query optimiser folds the constant <c>true</c> away, so it costs nothing in SQL.
    /// </remarks>
    public static Expression<Func<T, bool>> True<T>() => _ => true;

    /// <summary>A predicate matching nothing. The identity element for <see cref="Or{T}"/>.</summary>
    public static Expression<Func<T, bool>> False<T>() => _ => false;

    /// <summary>
    /// Swaps one parameter node for another throughout an expression tree.
    /// </summary>
    /// <remarks>
    /// <see cref="ExpressionVisitor"/> implements the visitor pattern over every node kind.
    /// Override only the node type you care about and the base class handles the recursion —
    /// producing a new tree rather than mutating the old one, since expression trees are immutable.
    /// </remarks>
    private sealed class ParameterReplacer(ParameterExpression from, ParameterExpression to)
        : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == from ? to : base.VisitParameter(node);
    }
}
