using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Serialization;

namespace DotBoxD.Queryable.Analysis;

/// <summary>
/// Detects contradictions among the leaves of a conjunction: a term together with its negation
/// (<c>x &amp;&amp; !x</c>), and, per field, conflicting equalities, an equality contradicted by an inequality,
/// and empty or equality-excluding numeric ranges. Case-insensitive comparisons are skipped (treated as
/// non-contradictory) to stay sound — the detector never reports a contradiction it is not certain of.
/// </summary>
internal static class ConjunctionContradictions
{
    public static bool Detect(IReadOnlyList<QueryFilter> children)
    {
        if (HasTermAndNegation(children))
        {
            return true;
        }

        var byField = new Dictionary<string, FieldConstraints>(StringComparer.Ordinal);
        foreach (var child in children)
        {
            if (child.Kind != QueryFilterKind.Compare || child.Value is null || child.IgnoreCase)
            {
                continue;
            }

            if (!byField.TryGetValue(child.Field, out var constraints))
            {
                constraints = new FieldConstraints();
                byField[child.Field] = constraints;
            }

            constraints.Add(child.Operator, child.Value);
        }

        return byField.Values.Any(c => c.IsContradictory());
    }

    private static bool HasTermAndNegation(IReadOnlyList<QueryFilter> children)
    {
        var signatures = new HashSet<string>(StringComparer.Ordinal);
        foreach (var child in children)
        {
            signatures.Add(QueryFingerprint.CanonicalText(child));
        }

        foreach (var child in children)
        {
            if (child.Kind == QueryFilterKind.Not &&
                signatures.Contains(QueryFingerprint.CanonicalText(child.Children[0])))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class FieldConstraints
    {
        private readonly List<QueryValue> _equalities = [];
        private readonly List<QueryValue> _notEquals = [];
        private NumericLiteral? _lower;
        private bool _lowerStrict;
        private NumericLiteral? _upper;
        private bool _upperStrict;

        public void Add(QueryComparisonOperator op, QueryValue value)
        {
            switch (op)
            {
                case QueryComparisonOperator.Equal:
                    _equalities.Add(value);
                    break;
                case QueryComparisonOperator.NotEqual:
                    _notEquals.Add(value);
                    break;
                case QueryComparisonOperator.GreaterThan:
                case QueryComparisonOperator.GreaterThanOrEqual:
                    Tighten(ref _lower, ref _lowerStrict, value, op == QueryComparisonOperator.GreaterThan, takeMax: true);
                    break;
                case QueryComparisonOperator.LessThan:
                case QueryComparisonOperator.LessThanOrEqual:
                    Tighten(ref _upper, ref _upperStrict, value, op == QueryComparisonOperator.LessThan, takeMax: false);
                    break;
                default:
                    break;
            }
        }

        public bool IsContradictory()
            => HasEqualityConflict() || HasEqualityVersusInequality() || HasEmptyRange() || HasEqualityOutsideRange();

        private bool HasEqualityConflict()
        {
            for (var i = 0; i < _equalities.Count; i++)
            {
                for (var j = i + 1; j < _equalities.Count; j++)
                {
                    if (AreEqual(_equalities[i], _equalities[j]) == false)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool HasEqualityVersusInequality()
            => _equalities.Any(e => _notEquals.Any(n => AreEqual(e, n) == true));

        private bool HasEmptyRange()
        {
            if (_lower is not { } lo || _upper is not { } hi)
            {
                return false;
            }

            var comparison = lo.CompareTo(hi);
            return comparison is > 0 || (comparison is 0 && (_lowerStrict || _upperStrict));
        }

        private bool HasEqualityOutsideRange()
        {
            foreach (var equality in _equalities)
            {
                if (TryNumber(equality) is not { } value)
                {
                    continue;
                }

                var lowerComparison = _lower is { } lo ? value.CompareTo(lo) : null;
                if (lowerComparison is < 0 || (lowerComparison is 0 && _lowerStrict))
                {
                    return true;
                }

                var upperComparison = _upper is { } hi ? value.CompareTo(hi) : null;
                if (upperComparison is > 0 || (upperComparison is 0 && _upperStrict))
                {
                    return true;
                }
            }

            return false;
        }

        private static void Tighten(ref NumericLiteral? bound, ref bool strict, QueryValue value, bool isStrict, bool takeMax)
        {
            if (TryNumber(value) is not { } number)
            {
                return;
            }

            if (bound is not { } current)
            {
                bound = number;
                strict = isStrict;
                return;
            }

            var comparison = number.CompareTo(current);
            if (comparison is not { } order)
            {
                return;
            }

            var tighter = takeMax ? order > 0 : order < 0;
            if (tighter)
            {
                bound = number;
                strict = isStrict;
            }
            else if (order == 0 && isStrict)
            {
                // Same bound value, but this constraint is strict (> vs >=): the stricter wins.
                strict = true;
            }
        }

        private static bool? AreEqual(QueryValue a, QueryValue b)
        {
            if (TryNumber(a) is { } na && TryNumber(b) is { } nb)
            {
                return na.CompareTo(nb) is { } comparison ? comparison == 0 : null;
            }

            if (a.Kind != b.Kind)
            {
                return null;
            }

            return a.Kind switch
            {
                QueryValueKind.String => string.Equals(a.String, b.String, StringComparison.Ordinal),
                QueryValueKind.Boolean => a.Boolean == b.Boolean,
                QueryValueKind.Null => true,
                _ => null,
            };
        }

        private static NumericLiteral? TryNumber(QueryValue value) => value.Kind switch
        {
            QueryValueKind.Integer => NumericLiteral.FromExact(value.Integer),
            QueryValueKind.UnsignedInteger => NumericLiteral.FromExact(value.UnsignedInteger),
            QueryValueKind.Decimal => NumericLiteral.FromExact(value.Decimal),
            QueryValueKind.Number => NumericLiteral.FromFloating(value.Number),
            _ => null,
        };

        private readonly record struct NumericLiteral(decimal Exact, double Floating, bool IsFloating)
        {
            public static NumericLiteral FromExact(decimal value) => new(value, 0, IsFloating: false);

            public static NumericLiteral FromFloating(double value) => new(0, value, IsFloating: true);

            public int? CompareTo(NumericLiteral other)
            {
                if (IsFloating != other.IsFloating)
                {
                    // The AST does not know the member's CLR type, so exact-vs-floating contradictions are not certain.
                    return null;
                }

                return IsFloating
                    ? Floating.CompareTo(other.Floating)
                    : Exact.CompareTo(other.Exact);
            }
        }
    }
}
