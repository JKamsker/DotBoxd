using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Translation;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class ExpressionQueryTranslatorTests
{
    [Fact]
    public void TranslateFilter_maps_comparison_to_compare_node()
    {
        var filter = ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => e.Damage >= 5);

        Assert.Equal(QueryFilterKind.Compare, filter.Kind);
        Assert.Equal("Damage", filter.Field);
        Assert.Equal(QueryComparisonOperator.GreaterThanOrEqual, filter.Operator);
        Assert.Equal(QueryValueKind.Integer, filter.Value!.Kind);
        Assert.Equal(5, filter.Value!.Integer);
    }

    [Fact]
    public void TranslateFilter_maps_string_equality()
    {
        var filter = ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => e.AttackerId == "player-1");

        Assert.Equal(QueryComparisonOperator.Equal, filter.Operator);
        Assert.Equal("player-1", filter.Value!.String);
    }

    [Fact]
    public void TranslateFilter_maps_and_or_not()
    {
        var and = ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => e.AttackerId == "p" && e.Damage >= 5);
        Assert.Equal(QueryFilterKind.And, and.Kind);
        Assert.Equal(2, and.Children.Count);

        var or = ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => e.Damage >= 5 || e.AttackerLevel > 10);
        Assert.Equal(QueryFilterKind.Or, or.Kind);

        var not = ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => !(e.Damage >= 5));
        Assert.Equal(QueryFilterKind.Not, not.Kind);
        Assert.Single(not.Children);
    }

    [Fact]
    public void TranslateFilter_maps_boolean_member()
    {
        var filter = ExpressionQueryTranslator.TranslateFilter<SourcedTestEvent>(e => e.Flagged);

        Assert.Equal(QueryFilterKind.Compare, filter.Kind);
        Assert.Equal("Flagged", filter.Field);
        Assert.Equal(QueryComparisonOperator.Equal, filter.Operator);
        Assert.True(filter.Value!.Boolean);
    }

    [Fact]
    public void TranslateFilter_flips_operator_when_constant_is_on_the_left()
    {
        var filter = ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => 5 <= e.Damage);

        Assert.Equal("Damage", filter.Field);
        Assert.Equal(QueryComparisonOperator.GreaterThanOrEqual, filter.Operator);
        Assert.Equal(5, filter.Value!.Integer);
    }

    [Fact]
    public void TranslateFilter_resolves_captured_variable_and_records_parameter_key()
    {
        var minimum = 5;
        var filter = ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => e.Damage >= minimum);

        Assert.Equal(5, filter.Value!.Integer);
        Assert.Equal("p0", filter.Value!.ParameterKey);
    }

    [Fact]
    public void TranslateFilter_supports_nested_member_path()
    {
        var filter = ExpressionQueryTranslator.TranslateFilter<SourcedTestEvent>(e => e.Source.Id == "abc");

        Assert.Equal("Source.Id", filter.Field);
        Assert.Equal("abc", filter.Value!.String);
    }

    [Fact]
    public void TranslateFilter_supports_direct_nullable_value_comparison()
    {
        var value = ExpressionQueryTranslator.TranslateFilter<NullableValueTestEvent>(e => e.Score == 5);

        Assert.Equal("Score", value.Field);
        Assert.Equal(QueryComparisonOperator.Equal, value.Operator);
        Assert.Equal(QueryValueKind.Integer, value.Value!.Kind);
        Assert.Equal(5, value.Value.Integer);

        var nullValue = ExpressionQueryTranslator.TranslateFilter<NullableValueTestEvent>(e => e.Score == null);

        Assert.Equal("Score", nullValue.Field);
        Assert.Equal(QueryValueKind.Null, nullValue.Value!.Kind);
    }

    [Fact]
    public void TranslateFilter_rejects_nullable_value_members()
    {
        var hasValue = Assert.Throws<QueryTranslationException>(
            () => ExpressionQueryTranslator.TranslateFilter<NullableValueTestEvent>(e => e.Score.HasValue));
        Assert.Contains("Nullable", hasValue.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("compare the nullable member directly", hasValue.Message, StringComparison.OrdinalIgnoreCase);

#pragma warning disable CS8629 // Intentionally author .Value to verify translation rejects it before runtime.
        var value = Assert.Throws<QueryTranslationException>(
            () => ExpressionQueryTranslator.TranslateFilter<NullableValueTestEvent>(e => e.Score.Value == 5));
#pragma warning restore CS8629
        Assert.Contains("Nullable", value.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("compare the nullable member directly", value.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TranslateFilter_rejects_lossy_member_path_casts()
    {
        var ex = Assert.Throws<QueryTranslationException>(
            () => ExpressionQueryTranslator.TranslateFilter<MetricTestEvent>(e => (int)e.Score == 1));

        Assert.Contains("cast", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TranslateFilter_maps_string_methods_with_case_sensitivity()
    {
        var contains = ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => e.TargetId.Contains("pl"));
        Assert.Equal(QueryComparisonOperator.StringContains, contains.Operator);
        Assert.False(contains.IgnoreCase);

        var startsWith = ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(
            e => e.TargetId.StartsWith("PL", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(QueryComparisonOperator.StringStartsWith, startsWith.Operator);
        Assert.True(startsWith.IgnoreCase);
    }

    [Fact]
    public void TranslateFilter_maps_static_string_equals_with_ordinal_ignore_case()
    {
        var filter = ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(
            e => string.Equals(e.TargetId, "PL", StringComparison.OrdinalIgnoreCase));

        Assert.Equal("TargetId", filter.Field);
        Assert.Equal(QueryComparisonOperator.Equal, filter.Operator);
        Assert.True(filter.IgnoreCase);
        Assert.Equal("PL", filter.Value!.String);

        var reversed = ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(
            e => string.Equals("PL", e.TargetId, StringComparison.Ordinal));

        Assert.Equal("TargetId", reversed.Field);
        Assert.Equal(QueryComparisonOperator.Equal, reversed.Operator);
        Assert.False(reversed.IgnoreCase);
        Assert.Equal("PL", reversed.Value!.String);
    }

    [Fact]
    public void TranslateFilter_maps_collection_contains_to_in()
    {
        var ids = new[] { "a", "b", "c" };
        var filter = ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => ids.Contains(e.AttackerId));

        Assert.Equal(QueryFilterKind.In, filter.Kind);
        Assert.Equal("AttackerId", filter.Field);
        Assert.Equal(3, filter.Values.Count);
    }

    [Fact]
    public void TranslateFilter_rejects_arithmetic_with_clear_diagnostic()
    {
        var ex = Assert.Throws<QueryTranslationException>(
            () => ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => e.Damage + 1 > 5));
        Assert.Contains("comparison", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TranslateFilter_rejects_member_to_member_comparison()
    {
        Assert.Throws<QueryTranslationException>(
            () => ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => e.AttackerId == e.TargetId));
    }

    [Fact]
    public void TranslateProjection_handles_identity_member_and_construction()
    {
        var identity = ExpressionQueryTranslator.TranslateProjection<AttackTestEvent, AttackTestEvent>(e => e);
        Assert.Equal(QueryProjectionKind.Identity, identity.Kind);

        var member = ExpressionQueryTranslator.TranslateProjection<AttackTestEvent, string>(e => e.AttackerId);
        Assert.Equal(QueryProjectionKind.Member, member.Kind);
        Assert.Equal("AttackerId", member.Path);

        var construct = ExpressionQueryTranslator.TranslateProjection<AttackTestEvent, AttackNotice>(
            e => new AttackNotice(e.AttackerId, e.TargetId, e.Damage));
        Assert.Equal(QueryProjectionKind.Construct, construct.Kind);
        Assert.Equal(3, construct.Fields.Count);
        Assert.Equal(new[] { "AttackerId", "TargetId", "Damage" }, construct.Fields.Select(f => f.Name));
        Assert.All(construct.Fields, f => Assert.NotNull(f.Path));
    }

    [Fact]
    public void TranslateProjection_supports_constant_and_anonymous_members()
    {
        var withConstant = ExpressionQueryTranslator.TranslateProjection<AttackTestEvent, AttackNotice>(
            e => new AttackNotice(e.AttackerId, "fixed-target", e.Damage));
        var targetField = withConstant.Fields.Single(f => f.Name == "TargetId");
        Assert.Null(targetField.Path);
        Assert.Equal("fixed-target", targetField.Constant!.String);

        var anonymous = ExpressionQueryTranslator.TranslateProjection(
            (AttackTestEvent e) => new { e.AttackerId, Dmg = e.Damage });
        Assert.Equal(QueryProjectionKind.Construct, anonymous.Kind);
        Assert.Equal(new[] { "AttackerId", "Dmg" }, anonymous.Fields.Select(f => f.Name));
    }

    [Fact]
    public void TranslateProjection_preserves_member_init_constructor_arguments()
    {
        var projection = ExpressionQueryTranslator.TranslateProjection<AttackTestEvent, MutableAttackNotice>(
            e => new MutableAttackNotice(e.AttackerId)
            {
                TargetId = e.TargetId,
                Damage = e.Damage,
            });

        Assert.Equal(QueryProjectionKind.Construct, projection.Kind);
        Assert.Equal(new[] { "attackerId", "TargetId", "Damage" }, projection.Fields.Select(f => f.Name));
        Assert.Equal(new[] { "AttackerId", "TargetId", "Damage" }, projection.Fields.Select(f => f.Path));
    }

    [Fact]
    public void TranslateProjection_rejects_unsupported_shape()
    {
        Assert.Throws<QueryTranslationException>(
            () => ExpressionQueryTranslator.TranslateProjection<AttackTestEvent, int>(e => e.Damage + 1));
    }

    [Fact]
    public void TranslateProjection_rejects_lossy_member_path_casts()
    {
        var ex = Assert.Throws<QueryTranslationException>(
            () => ExpressionQueryTranslator.TranslateProjection<MetricTestEvent, int>(e => (int)e.Score));

        Assert.Contains("cast", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TranslateFilter_rejects_non_finite_constant()
    {
        var nonFinite = double.NaN;
        var ex = Assert.Throws<QueryTranslationException>(
            () => ExpressionQueryTranslator.TranslateFilter<MetricTestEvent>(e => e.Score == nonFinite));
        Assert.Contains("non-finite", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void QueryValue_rejects_non_finite_numbers()
    {
        Assert.Throws<ArgumentException>(() => QueryValue.FromNumber(double.NaN));
        Assert.Throws<ArgumentException>(() => QueryValue.FromNumber(double.PositiveInfinity));
    }

    [Fact]
    public void QueryFilter_rejects_non_identifier_field_paths()
    {
        Assert.Throws<ArgumentException>(
            () => QueryFilter.Compare("1bad", QueryComparisonOperator.Equal, QueryValue.FromInteger(1)));
        Assert.Throws<ArgumentException>(
            () => QueryFilter.Compare("has space", QueryComparisonOperator.Equal, QueryValue.FromInteger(1)));

        // A dotted identifier path is valid.
        var ok = QueryFilter.Compare("Source.Id", QueryComparisonOperator.Equal, QueryValue.FromString("x"));
        Assert.Equal("Source.Id", ok.Field);
    }
}
