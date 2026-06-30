using System.Linq.Expressions;
using System.Reflection;
using DotBoxD.Queryable.Ast;

namespace DotBoxD.Queryable.Execution;

/// <summary>
/// Compiles a portable <see cref="QueryFilter"/> into a delegate for the hot-path tier. The compiled tree
/// calls the very same <see cref="MemberValueReader"/> and <see cref="QueryValueComparer"/> primitives the
/// interpreter uses, so a promoted query produces identical results — only the per-node tree walk and kind
/// switch are removed. Use it to promote frequently-evaluated filters; the interpreter remains the cold,
/// limit-checked default.
/// </summary>
public static class QueryFilterCompiler
{
    private static readonly MethodInfo ReadMethod =
        typeof(MemberValueReader).GetMethod(nameof(MemberValueReader.Read))!;

    private static readonly MethodInfo CompareMethod =
        typeof(QueryValueComparer).GetMethod(nameof(QueryValueComparer.Compare))!;

    private static readonly MethodInfo IsAnyEqualMethod =
        typeof(QueryValueComparer).GetMethod(nameof(QueryValueComparer.IsAnyEqual))!;

    /// <summary>Compiles <paramref name="filter"/> to a predicate over a (boxed) event object.</summary>
    public static Func<object, bool> Compile(QueryFilter filter, MemberValueReader reader)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(reader);
        var parameter = Expression.Parameter(typeof(object), "e");
        var body = Build(filter, parameter, Expression.Constant(reader));
        return Expression.Lambda<Func<object, bool>>(body, parameter).Compile();
    }

    private static Expression Build(QueryFilter filter, ParameterExpression target, Expression reader) => filter.Kind switch
    {
        QueryFilterKind.MatchAll => Expression.Constant(true),
        QueryFilterKind.And => Fold(filter.Children, target, reader, Expression.AndAlso, identity: true),
        QueryFilterKind.Or => Fold(filter.Children, target, reader, Expression.OrElse, identity: false),
        QueryFilterKind.Not => Expression.Not(Build(filter.Children[0], target, reader)),
        QueryFilterKind.Compare => CompareExpression(filter, target, reader),
        QueryFilterKind.In => InExpression(filter, target, reader),
        _ => Expression.Constant(false),
    };

    private static Expression Fold(
        IReadOnlyList<QueryFilter> children,
        ParameterExpression target,
        Expression reader,
        Func<Expression, Expression, Expression> combine,
        bool identity)
    {
        Expression? accumulator = null;
        foreach (var child in children)
        {
            var compiled = Build(child, target, reader);
            accumulator = accumulator is null ? compiled : combine(accumulator, compiled);
        }

        return accumulator ?? Expression.Constant(identity);
    }

    private static Expression Read(QueryFilter filter, ParameterExpression target, Expression reader)
        => Expression.Call(reader, ReadMethod, target, Expression.Constant(filter.Field));

    private static Expression CompareExpression(QueryFilter filter, ParameterExpression target, Expression reader)
        => Expression.Call(
            CompareMethod,
            Read(filter, target, reader),
            Expression.Constant(filter.Operator),
            Expression.Constant(QueryFilterInvariants.CompareValue(filter)),
            Expression.Constant(filter.IgnoreCase));

    private static Expression InExpression(QueryFilter filter, ParameterExpression target, Expression reader)
        => Expression.Call(
            IsAnyEqualMethod,
            Read(filter, target, reader),
            Expression.Constant(filter.Values, typeof(IReadOnlyList<QueryValue>)),
            Expression.Constant(filter.IgnoreCase));
}
