using System.Collections;
using System.Linq.Expressions;
using DotBoxD.Queryable.Ast;

namespace DotBoxD.Queryable.Translation;

/// <summary>
/// Evaluates the constant/closure side of a query expression into portable <see cref="QueryValue"/>s.
/// A captured local, field, or literal is resolved once at translation time (the expression is parameter-
/// free), so the wire model carries plain values rather than closure plumbing. Unsupported value types
/// raise a <see cref="QueryTranslationException"/> with an actionable message.
/// </summary>
internal static class QueryValueFactory
{
    /// <summary>
    /// Evaluates a parameter-free subexpression to a CLR object. Returns <see langword="false"/> when the
    /// expression references <paramref name="parameter"/> (and therefore is not a constant operand).
    /// </summary>
    public static bool TryEvaluateObject(Expression expression, ParameterExpression parameter, out object? result)
    {
        if (MemberPathReader.ReferencesParameter(expression, parameter))
        {
            result = null;
            return false;
        }

        var stripped = MemberPathReader.StripConvert(expression);
        if (stripped is ConstantExpression constant)
        {
            result = constant.Value;
            return true;
        }

        try
        {
            // Compile the operand as-is (no manual Convert-to-object) and let DynamicInvoke box the
            // result; this is the robust partial-evaluation pattern used by LINQ providers.
            result = Expression.Lambda(expression).Compile().DynamicInvoke();
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            throw new QueryTranslationException(
                $"Could not evaluate the constant operand '{expression}'.", ex);
        }
    }

    /// <summary>Converts a resolved CLR object into a <see cref="QueryValue"/>, throwing for unsupported types.</summary>
    public static QueryValue ToValue(object? raw, Expression source)
    {
        if (QueryValue.TryFromObject(raw, out var value))
        {
            return value;
        }

        if (raw is double d && !double.IsFinite(d) || raw is float f && !float.IsFinite(f))
        {
            throw new QueryTranslationException(
                $"Non-finite numeric values (NaN, Infinity) are not supported in '{source}'.");
        }

        throw new QueryTranslationException(
            $"Unsupported constant value type '{raw?.GetType().Name}' in '{source}'. " +
            "Only bool, integral and floating types, string, and enums are supported.");
    }

    /// <summary>Evaluates a parameter-free collection operand into a list of <see cref="QueryValue"/>s.</summary>
    public static IReadOnlyList<QueryValue> ToValues(Expression expression, ParameterExpression parameter)
    {
        if (!TryEvaluateObject(expression, parameter, out var raw) || raw is not IEnumerable enumerable || raw is string)
        {
            throw QueryTranslationException.Unsupported(
                expression, "the 'in'/Contains operand must be a constant array or collection.");
        }

        var values = new List<QueryValue>();
        foreach (var item in enumerable)
        {
            values.Add(ToValue(item, expression));
        }

        return values;
    }
}
