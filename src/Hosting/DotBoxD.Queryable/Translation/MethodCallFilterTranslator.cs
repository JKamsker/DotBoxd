using System.Linq.Expressions;
using DotBoxD.Queryable.Ast;

namespace DotBoxD.Queryable.Translation;

/// <summary>
/// Translates the supported method-call predicates: ordinal string <c>Contains</c>/<c>StartsWith</c>/
/// <c>EndsWith</c>/<c>Equals</c> (with an optional <see cref="StringComparison"/> selecting case
/// sensitivity) against a constant, and <c>Contains</c> over a constant collection (lowered to
/// <see cref="QueryFilterKind.In"/>). The <c>makeValue</c> callback assigns capture ordinals.
/// </summary>
internal static class MethodCallFilterTranslator
{
    public static QueryFilter Translate(
        MethodCallExpression call,
        ParameterExpression parameter,
        Func<object?, Expression, QueryValue> makeValue)
    {
        if (TryTranslateString(call, parameter, makeValue, out var stringFilter))
        {
            return stringFilter;
        }

        if (TryTranslateContains(call, parameter, out var inFilter))
        {
            return inFilter;
        }

        throw QueryTranslationException.Unsupported(
            call,
            "supported calls are string Contains/StartsWith/EndsWith/Equals and Contains over a constant collection.");
    }

    private static bool TryTranslateString(
        MethodCallExpression call,
        ParameterExpression parameter,
        Func<object?, Expression, QueryValue> makeValue,
        out QueryFilter filter)
    {
        filter = QueryFilter.MatchAll;
        if (call.Method.DeclaringType != typeof(string))
        {
            return false;
        }

        if (call.Object is null)
        {
            return TryTranslateStaticStringEquals(call, parameter, makeValue, out filter);
        }

        if (!MemberPathReader.TryReadPath(call.Object, parameter, out var path) ||
            call.Arguments.Count is not 1 and not 2 ||
            !QueryValueFactory.TryEvaluateObject(call.Arguments[0], parameter, out var raw) ||
            raw is not string)
        {
            return false;
        }

        var op = call.Method.Name switch
        {
            nameof(string.Contains) => QueryComparisonOperator.StringContains,
            nameof(string.StartsWith) => QueryComparisonOperator.StringStartsWith,
            nameof(string.EndsWith) => QueryComparisonOperator.StringEndsWith,
            nameof(string.Equals) => QueryComparisonOperator.Equal,
            _ => (QueryComparisonOperator)(-1),
        };

        if ((int)op < 0)
        {
            return false;
        }

        var comparisonArgument = call.Arguments.Count == 2 ? call.Arguments[1] : null;
        var ignoreCase = ReadIgnoreCase(call, op, comparisonArgument, parameter);
        filter = QueryFilter.Compare(path, op, makeValue(raw, call.Arguments[0]), ignoreCase);
        return true;
    }

    private static bool TryTranslateStaticStringEquals(
        MethodCallExpression call,
        ParameterExpression parameter,
        Func<object?, Expression, QueryValue> makeValue,
        out QueryFilter filter)
    {
        filter = QueryFilter.MatchAll;
        if (call.Method.Name != nameof(string.Equals) ||
            call.Arguments.Count is not 2 and not 3 ||
            !TryReadStringEqualsOperands(
                call.Arguments[0],
                call.Arguments[1],
                parameter,
                out var path,
                out var valueExpression,
                out var raw) ||
            raw is not string)
        {
            return false;
        }

        var comparisonArgument = call.Arguments.Count == 3 ? call.Arguments[2] : null;
        var ignoreCase = ReadIgnoreCase(call, QueryComparisonOperator.Equal, comparisonArgument, parameter);
        filter = QueryFilter.Compare(
            path,
            QueryComparisonOperator.Equal,
            makeValue(raw, valueExpression),
            ignoreCase);
        return true;
    }

    private static bool TryReadStringEqualsOperands(
        Expression left,
        Expression right,
        ParameterExpression parameter,
        out string path,
        out Expression valueExpression,
        out object? raw)
    {
        if (MemberPathReader.TryReadPath(left, parameter, out path) &&
            QueryValueFactory.TryEvaluateObject(right, parameter, out raw))
        {
            valueExpression = right;
            return true;
        }

        if (MemberPathReader.TryReadPath(right, parameter, out path) &&
            QueryValueFactory.TryEvaluateObject(left, parameter, out raw))
        {
            valueExpression = left;
            return true;
        }

        path = "";
        valueExpression = left;
        raw = null;
        return false;
    }

    private static bool TryTranslateContains(
        MethodCallExpression call,
        ParameterExpression parameter,
        out QueryFilter filter)
    {
        filter = QueryFilter.MatchAll;
        if (call.Method.Name != nameof(Enumerable.Contains))
        {
            return false;
        }

        // Static Contains(source, item) — Enumerable.Contains or MemoryExtensions.Contains(ReadOnlySpan, item)
        // — or instance ICollection<T>.Contains(item).
        var (collection, item) = call.Object is null
            ? (call.Arguments.Count == 2 ? call.Arguments[0] : null, call.Arguments.Count == 2 ? call.Arguments[1] : null)
            : (call.Object, call.Arguments.Count == 1 ? call.Arguments[0] : null);

        if (collection is null || item is null || !MemberPathReader.TryReadPath(item, parameter, out var path))
        {
            return false;
        }

        var unwrapped = UnwrapSpan(collection);

        // HashSet/Dictionary-style collections can carry a custom equality comparer that changes membership
        // semantics even when written as static Enumerable.Contains(source, item); lowering that to a plain
        // In would silently drop or add matches. Reject custom/culture-sensitive comparers rather than
        // mis-translate.
        if (QueryValueFactory.TryEvaluateObject(unwrapped, parameter, out var collectionObject) &&
            collectionObject is not null &&
            CollectionComparerSupport.HasUnsupportedComparer(collectionObject))
        {
            throw QueryTranslationException.Unsupported(
                call,
                "Contains over a collection with a custom, case-insensitive, or culture-sensitive comparer is not supported; use a default/ordinal collection.");
        }

        filter = QueryFilter.In(path, QueryValueFactory.ToValues(unwrapped, parameter));
        return true;
    }

    // `array.Contains(x)` binds to MemoryExtensions.Contains(ReadOnlySpan<T>, T); the source then appears as
    // an implicit T[] -> ReadOnlySpan<T> conversion (an op_Implicit call or Convert) wrapping the real
    // collection. Unwrap it so the underlying array/collection can be evaluated as a constant.
    private static Expression UnwrapSpan(Expression collection)
    {
        var stripped = MemberPathReader.StripConvert(collection);
        if (stripped is MethodCallExpression { Method.Name: "op_Implicit" } conversion)
        {
            var operand = conversion.Object ?? (conversion.Arguments.Count == 1 ? conversion.Arguments[0] : null);
            if (operand is not null)
            {
                return operand;
            }
        }

        return stripped;
    }

    private static bool ReadIgnoreCase(
        MethodCallExpression call,
        QueryComparisonOperator op,
        Expression? comparisonArgument,
        ParameterExpression parameter)
    {
        if (comparisonArgument is null)
        {
            if (op is QueryComparisonOperator.StringStartsWith or QueryComparisonOperator.StringEndsWith)
            {
                throw QueryTranslationException.Unsupported(
                    call,
                    $"string {call.Method.Name} one-argument overload is culture-sensitive; pass StringComparison.Ordinal or OrdinalIgnoreCase.");
            }

            return false;
        }

        if (QueryValueFactory.TryEvaluateObject(comparisonArgument, parameter, out var raw) &&
            raw is StringComparison comparison)
        {
            // The evaluator compares ordinally, so only the ordinal modes can be honored faithfully. A
            // culture-sensitive overload would silently change semantics — reject it instead of downgrading.
            return comparison switch
            {
                StringComparison.Ordinal => false,
                StringComparison.OrdinalIgnoreCase => true,
                _ => throw QueryTranslationException.Unsupported(
                    call,
                    $"StringComparison.{comparison} is culture-sensitive; only Ordinal and OrdinalIgnoreCase are supported."),
            };
        }

        throw QueryTranslationException.Unsupported(
            call,
            "string filters support only ordinal overloads: one-argument Contains/Equals, or StringComparison.Ordinal/OrdinalIgnoreCase.");
    }

}
