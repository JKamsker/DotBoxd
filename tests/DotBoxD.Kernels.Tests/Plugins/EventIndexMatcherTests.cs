using DotBoxD.Plugins;
using DotBoxD.Plugins.Indexing;

namespace DotBoxD.Kernels.Tests.Plugins;

/// <summary>
/// Issue #50: the framework <see cref="EventIndexMatcher{TEvent}"/> compiles manifest predicates into cheap
/// checks over precompiled getters. These tests pin its soundness contract — most importantly that it stays
/// robust when an (untrusted) manifest's value type drifts from the indexed property's CLR type: it must
/// neither throw nor wrongly reject a genuinely matching event, keeping "index-reject ⇒ verified-IR-reject".
/// </summary>
public sealed class EventIndexMatcherTests
{
    private sealed record IndexedSample(
        [property: EventIndexKey] string Name,
        [property: EventIndexKey] int Count,
        [property: EventIndexKey] double Ratio,
        [property: EventIndexKey] string? Tag,
        int Unindexed);

    private static IndexedSample Sample(
        string name = "alpha", int count = 5, double ratio = 1.5, string? tag = "t", int unindexed = 0)
        => new(name, count, ratio, tag, unindexed);

    [Fact]
    public void Honors_only_event_index_key_properties()
    {
        Assert.Equivalent(
            new HashSet<string> { "Name", "Count", "Ratio", "Tag" },
            new HashSet<string>(EventIndexMatcher<IndexedSample>.IndexKeyNames));

        var matcher = EventIndexMatcher<IndexedSample>.Create(
        [
            new IndexedPredicate("Name", IndexPredicateOperator.Equals, "alpha", "string"),
            new IndexedPredicate("Unindexed", IndexPredicateOperator.Equals, 0, "int"),
        ]);

        var honored = Assert.Single(matcher.HonoredPredicates);
        Assert.Equal("Name", honored.Path);
    }

    [Fact]
    public void Evaluates_equality_and_range_over_matching_types()
    {
        var matcher = EventIndexMatcher<IndexedSample>.Create(
        [
            new IndexedPredicate("Name", IndexPredicateOperator.Equals, "alpha", "string"),
            new IndexedPredicate("Count", IndexPredicateOperator.GreaterThanOrEqual, 5, "int"),
        ]);

        Assert.True(matcher.CouldMatch(Sample(name: "alpha", count: 5)));
        Assert.True(matcher.CouldMatch(Sample(name: "alpha", count: 9)));
        Assert.False(matcher.CouldMatch(Sample(name: "beta", count: 9)));
        Assert.False(matcher.CouldMatch(Sample(name: "alpha", count: 4)));
    }

    [Fact]
    public void Reconciles_a_numeric_value_whose_boxed_type_differs_from_the_property()
    {
        // A manifest value boxed as long (5L) against an int property must still match Count == 5 — the
        // pre-hardening object.Equals(boxedInt, boxedLong) returned false and silently dropped the match.
        var matcher = EventIndexMatcher<IndexedSample>.Create(
        [
            new IndexedPredicate("Count", IndexPredicateOperator.Equals, 5L, "long"),
        ]);

        Assert.True(matcher.CouldMatch(Sample(count: 5)));
        Assert.False(matcher.CouldMatch(Sample(count: 6)));
    }

    [Fact]
    public void Range_over_a_type_drifted_value_does_not_throw_and_compares_numerically()
    {
        // Pre-hardening, ((object)5).CompareTo((object)5L) threw ArgumentException — and the throw escaped the
        // dispatch loop. Now the value is reconciled to the property type and compared without throwing.
        var matcher = EventIndexMatcher<IndexedSample>.Create(
        [
            new IndexedPredicate("Count", IndexPredicateOperator.GreaterThan, 5L, "long"),
        ]);

        Assert.True(matcher.CouldMatch(Sample(count: 10)));
        Assert.False(matcher.CouldMatch(Sample(count: 3)));
    }

    [Fact]
    public void Drops_a_predicate_whose_value_type_cannot_be_reconciled_to_the_property()
    {
        // A string value against the int Count property cannot be reconciled, so the leaf is not honored and
        // is left entirely to the verified IR — never an unsound or throwing index check.
        var matcher = EventIndexMatcher<IndexedSample>.Create(
        [
            new IndexedPredicate("Count", IndexPredicateOperator.Equals, "not-a-number", "string"),
        ]);

        Assert.Empty(matcher.HonoredPredicates);
        Assert.False(matcher.HasIndex);
        Assert.True(matcher.CouldMatch(Sample(count: 999)));
    }

    [Fact]
    public void Null_reference_actual_rejects_equality_but_passes_ordering_through_to_the_verified_ir()
    {
        var equals = EventIndexMatcher<IndexedSample>.Create(
            [new IndexedPredicate("Tag", IndexPredicateOperator.Equals, "needle", "string")]);
        // null Tag is definitively != "needle" — a safe reject.
        Assert.False(equals.CouldMatch(Sample(tag: null)));
        Assert.True(equals.CouldMatch(Sample(tag: "needle")));

        var ordering = EventIndexMatcher<IndexedSample>.Create(
            [new IndexedPredicate("Tag", IndexPredicateOperator.LessThan, "m", "string")]);
        // null vs "m" ordering is undecidable, so the index must NOT reject — leave it to the verified IR.
        Assert.True(ordering.CouldMatch(Sample(tag: null)));
    }
}
