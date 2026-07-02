using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Serialization;
using DotBoxD.Queryable.Text;
using DotBoxD.Queryable.Translation;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryTextTests
{
    [Fact]
    public void Format_and_parse_round_trip_a_compound_filter()
    {
        var filter = ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(
            e => e.AttackerId == "player-1" && e.Damage >= 5);

        var text = QueryText.Format(filter);
        var parsed = QueryText.Parse(text);

        Assert.Equal(Fingerprint(filter), Fingerprint(parsed));
    }

    [Theory]
    [InlineData("AttackerId == \"player-1\"")]
    [InlineData("(AttackerId == \"a\" and Damage >= 5)")]
    [InlineData("(AttackerId == \"a\" or Damage < 2)")]
    [InlineData("not AttackerId == \"a\"")]
    [InlineData("AttackerId ~contains \"PL\"")]
    [InlineData("AttackerId in [\"a\", \"b\", \"c\"]")]
    [InlineData("Source.Id == \"abc\"")]
    [InlineData("*")]
    public void Parse_then_format_is_idempotent(string text)
    {
        var first = QueryText.Parse(text);
        var second = QueryText.Parse(QueryText.Format(first));
        Assert.Equal(Fingerprint(first), Fingerprint(second));
    }

    [Fact]
    public void Parse_maps_tokens_to_the_expected_ast()
    {
        var filter = QueryText.Parse("AttackerId == \"a\" and Damage >= 5");

        Assert.Equal(QueryFilterKind.And, filter.Kind);
        var attacker = filter.Children.Single(c => c.Field == "AttackerId");
        Assert.Equal(QueryComparisonOperator.Equal, attacker.Operator);
        Assert.Equal("a", attacker.Value!.String);
        var damage = filter.Children.Single(c => c.Field == "Damage");
        Assert.Equal(QueryComparisonOperator.GreaterThanOrEqual, damage.Operator);
        Assert.Equal(5, damage.Value!.Integer);
    }

    [Fact]
    public void Parse_recognizes_case_insensitive_operator()
    {
        var filter = QueryText.Parse("Name ~startswith \"pre\"");
        Assert.Equal(QueryComparisonOperator.StringStartsWith, filter.Operator);
        Assert.True(filter.IgnoreCase);
    }

    [Theory]
    [InlineData("AttackerId = \"a\"")]      // single '=' is invalid
    [InlineData("AttackerId == ")]          // missing value
    [InlineData("AttackerId blah \"a\"")]   // unknown operator
    [InlineData("(AttackerId == \"a\"")]    // unbalanced paren
    public void Parse_rejects_malformed_input(string text)
        => Assert.Throws<QueryTranslationException>(() => QueryText.Parse(text));

    [Fact]
    public void Parse_rejects_unknown_string_escape_sequences()
        => Assert.Throws<QueryTranslationException>(() => QueryText.Parse("Path == \"C:\\temp\""));

    [Fact]
    public void Parse_accepts_writer_supported_string_escape_sequences()
    {
        var filter = QueryText.Parse("Path == \"C:\\\\quoted\\\"name\"");

        Assert.Equal("C:\\quoted\"name", filter.Value!.String);
    }

    [Theory]
    [InlineData(1e21)]
    [InlineData(1.5e-10)]
    [InlineData(-3.5e-8)]
    [InlineData(1e-5)]
    public void Round_trips_exponent_notation_doubles(double value)
    {
        var filter = QueryFilter.Compare("X", QueryComparisonOperator.Equal, QueryValue.FromNumber(value));
        var parsed = QueryText.Parse(QueryText.Format(filter));
        Assert.Equal(Fingerprint(filter), Fingerprint(parsed));
    }

    private static string Fingerprint(QueryFilter filter)
        => QueryFingerprint.Compute(EventQueryDocument.Create("E", filter, QueryProjection.Identity));
}
