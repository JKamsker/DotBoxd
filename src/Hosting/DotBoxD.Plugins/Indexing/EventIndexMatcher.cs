using System.Globalization;
using System.Reflection;
using Expr = System.Linq.Expressions.Expression;

namespace DotBoxD.Plugins.Indexing;

/// <summary>
/// A host's compiled view of a subscription's <see cref="IndexedPredicate"/> metadata — the
/// "compile the metadata into whatever dispatch/index structure is natural for the runtime" half of
/// issue #47, promoted to the framework by issue #50. It keeps only the predicates whose
/// <see cref="IndexedPredicate.Path"/> is an <see cref="EventIndexKeyAttribute"/> property of
/// <typeparamref name="TEvent"/> (the fields the host indexes) and evaluates them cheaply against an event
/// through <b>precompiled property getters</b> — no per-event reflection, so the index never costs more
/// than it saves.
/// <para>
/// Because every kept predicate is a <i>necessary</i> AND condition of the real predicate,
/// <see cref="CouldMatch"/> returning <c>false</c> is always a safe reject; returning <c>true</c> means
/// the event passed the cheap index and the host should still run the verified IR unless the manifest
/// reported full coverage. Conversely, the matcher never rejects on a comparison it cannot soundly decide
/// (a value whose type cannot be reconciled to the property, a null reference, or an ordering it cannot
/// evaluate): such a leaf is dropped or passed through so the verified IR stays the authority. This keeps
/// the invariant "index-reject ⇒ verified-IR-reject" intact even for hand-built or imported manifests
/// whose <see cref="IndexedPredicate.ValueType"/> disagrees with the property's CLR type.
/// </para>
/// </summary>
public sealed class EventIndexMatcher<TEvent>
{
    // Built once per closed generic; the getters are compiled delegates, never PropertyInfo.GetValue.
    private static readonly IReadOnlyDictionary<string, IndexKey> IndexKeys = BuildIndexKeys();

    private readonly IReadOnlyList<IndexCheck> _checks;

    private EventIndexMatcher(IReadOnlyList<IndexCheck> checks, IReadOnlyList<IndexedPredicate> honored)
    {
        _checks = checks;
        HonoredPredicates = honored;
    }

    /// <summary>The manifest predicates this host can actually serve from an index (path is an index key).</summary>
    public IReadOnlyList<IndexedPredicate> HonoredPredicates { get; }

    /// <summary><c>true</c> when at least one manifest predicate mapped onto an indexed field.</summary>
    public bool HasIndex => _checks.Count > 0;

    /// <summary>The <see cref="EventIndexKeyAttribute"/> property names of <typeparamref name="TEvent"/>.</summary>
    public static IReadOnlyCollection<string> IndexKeyNames => (IReadOnlyCollection<string>)IndexKeys.Keys;

    /// <summary>
    /// Compiles <paramref name="predicates"/> into cheap index checks, keeping only those whose path is an
    /// <see cref="EventIndexKeyAttribute"/> property of <typeparamref name="TEvent"/> <i>and</i> whose value
    /// can be reconciled to that property's CLR type. A predicate whose value type cannot be reconciled is
    /// dropped (left to the verified IR) rather than turned into an unsound or throwing check.
    /// </summary>
    public static EventIndexMatcher<TEvent> Create(IReadOnlyList<IndexedPredicate> predicates)
    {
        ArgumentNullException.ThrowIfNull(predicates);
        var checks = new List<IndexCheck>();
        var honored = new List<IndexedPredicate>();
        foreach (var predicate in predicates)
        {
            if (IndexKeys.TryGetValue(predicate.Path, out var key) &&
                TryReconcile(predicate.Value, key.Type, out var value))
            {
                checks.Add(new IndexCheck(key.Getter, predicate.Operator, value));
                honored.Add(predicate);
            }
        }

        return new EventIndexMatcher<TEvent>(checks, honored);
    }

    /// <summary>
    /// Evaluates the cheap index checks against <paramref name="value"/>. Returns <c>false</c> as soon as
    /// any indexed constraint is definitively violated, so the host can skip dispatch entirely; a constraint
    /// it cannot decide is treated as satisfied so the verified IR remains the authority.
    /// </summary>
    public bool CouldMatch(TEvent value)
    {
        foreach (var check in _checks)
        {
            if (!check.Evaluate(value))
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyDictionary<string, IndexKey> BuildIndexKeys()
    {
        var keys = new Dictionary<string, IndexKey>(StringComparer.Ordinal);
        foreach (var property in typeof(TEvent).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.CanRead && property.GetCustomAttribute<EventIndexKeyAttribute>() is not null)
            {
                var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                keys[property.Name] = new IndexKey(CompileGetter(property), type);
            }
        }

        return keys;
    }

    // Builds value => (object?)value.Property once, so reading an indexed field is a delegate call rather
    // than reflection on every event.
    private static Func<TEvent, object?> CompileGetter(PropertyInfo property)
    {
        var parameter = Expr.Parameter(typeof(TEvent), "value");
        Expr instance = property.DeclaringType is { } declaring && !declaring.IsAssignableFrom(typeof(TEvent))
            ? Expr.Convert(parameter, declaring)
            : parameter;
        var boxed = Expr.Convert(Expr.Property(instance, property), typeof(object));
        return Expr.Lambda<Func<TEvent, object?>>(boxed, parameter).Compile();
    }

    // Coerces a manifest value to the indexed property's CLR type so every check compares like-typed boxed
    // operands. Exact-typed values pass through; numeric values are converted between int/long/double; any
    // other mismatch (e.g. a string value for an int property) fails so the leaf is dropped, not mis-served.
    private static bool TryReconcile(object? value, Type propertyType, out object? result)
    {
        result = value;
        if (value is null)
        {
            return false;
        }

        if (propertyType.IsInstanceOfType(value))
        {
            return true;
        }

        if (IsNumeric(propertyType) && IsNumeric(value.GetType()))
        {
            try
            {
                result = Convert.ChangeType(value, propertyType, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
            {
                return false;
            }
        }

        return false;
    }

    private static bool IsNumeric(Type type)
        => type == typeof(int) || type == typeof(long) || type == typeof(double) ||
           type == typeof(short) || type == typeof(byte) || type == typeof(float) || type == typeof(decimal);

    private readonly record struct IndexKey(Func<TEvent, object?> Getter, Type Type);

    private sealed class IndexCheck(Func<TEvent, object?> getter, IndexPredicateOperator op, object? value)
    {
        public bool Evaluate(TEvent target)
        {
            var actual = getter(target);
            return op switch
            {
                IndexPredicateOperator.Equals => Equals(actual, value),
                IndexPredicateOperator.NotEquals => !Equals(actual, value),
                IndexPredicateOperator.GreaterThan => TryCompare(actual, value, out var c) ? c > 0 : true,
                IndexPredicateOperator.GreaterThanOrEqual => TryCompare(actual, value, out var c) ? c >= 0 : true,
                IndexPredicateOperator.LessThan => TryCompare(actual, value, out var c) ? c < 0 : true,
                IndexPredicateOperator.LessThanOrEqual => TryCompare(actual, value, out var c) ? c <= 0 : true,
                // Unknown operator: cannot decide, so do not reject — leave it to the verified IR.
                _ => true,
            };
        }

        // Decidable only when both operands are non-null and the same CLR type (guaranteed by Create's
        // reconciliation for honored predicates). Anything else is left undecided so CouldMatch passes it
        // through to the verified IR rather than wrongly rejecting it.
        private static bool TryCompare(object? actual, object? expected, out int comparison)
        {
            comparison = 0;
            if (actual is null || expected is null || actual.GetType() != expected.GetType())
            {
                return false;
            }

            if (actual is IComparable comparable)
            {
                comparison = comparable.CompareTo(expected);
                return true;
            }

            return false;
        }
    }
}
