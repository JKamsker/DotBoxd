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
        if (call.Method.DeclaringType != typeof(string) || call.Object is null)
        {
            return false;
        }

        if (!MemberPathReader.TryReadPath(call.Object, parameter, out var path) || call.Arguments.Count == 0)
        {
            return false;
        }

        if (!QueryValueFactory.TryEvaluateObject(call.Arguments[0], parameter, out var raw) || raw is not string)
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

        var ignoreCase = IsIgnoreCase(call, call.Arguments, parameter);
        filter = QueryFilter.Compare(path, op, makeValue(raw, call.Arguments[0]), ignoreCase);
        return true;
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

        // An instance collection (HashSet/Dictionary/SortedSet) can carry a custom equality comparer that
        // changes membership semantics; lowering it to a case-sensitive ordinal In would silently drop or add
        // matches. Reject the case-insensitive / culture-sensitive comparers rather than mis-translate.
        if (call.Object is not null &&
            QueryValueFactory.TryEvaluateObject(unwrapped, parameter, out var collectionObject) &&
            collectionObject is not null &&
            HasNonOrdinalComparer(collectionObject))
        {
            throw QueryTranslationException.Unsupported(
                call,
                "Contains over a collection with a case-insensitive or culture-sensitive equality comparer is not supported; use a default/ordinal collection.");
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

    private static bool IsIgnoreCase(MethodCallExpression call, IReadOnlyList<Expression> arguments, ParameterExpression parameter)
    {
        for (var i = 1; i < arguments.Count; i++)
        {
            if (QueryValueFactory.TryEvaluateObject(arguments[i], parameter, out var raw) &&
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
        }

        return false;
    }

    private static bool HasNonOrdinalComparer(object collection)
    {
        if (collection.GetType().GetProperty("Comparer")?.GetValue(collection) is not IEqualityComparer<string> comparer)
        {
            return false;
        }

        // Behavioral probe rather than identity checks against the public singletons: an ordinal/default
        // comparer treats these pairs as distinct, while any case-insensitive or culture-sensitive comparer —
        // including factory-created ones via StringComparer.Create(...) — considers at least one pair equal, so
        // an ordinal In cannot reproduce its membership semantics. A default HashSet<string> is ordinal -> safe.
        return comparer.Equals("a", "A");
    }
}
