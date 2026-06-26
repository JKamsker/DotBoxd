namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

/// <summary>
/// Guard for instance <c>.Equals(...)</c> lowering: it must enforce the SAME scalar-only rule the <c>==</c> path
/// enforces. A list/map/record/array operand compares by STRUCTURE in the sandbox but by REFERENCE in C#, so
/// lowering <c>a.Equals(b)</c> on non-scalars would silently change a predicate's meaning. Non-scalar Equals must
/// fail safe (chain skipped); scalar and string Equals must keep lowering. Shares the
/// <see cref="RemoteRunLocalChainRuntimeTests"/> harness.
/// </summary>
public sealed partial class RemoteRunLocalChainRuntimeTests
{
    private const string ListEqualsSource = Prelude + """
        public static class ListEqualsUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.ScoreEvent>().Where(e => e.Scores.Equals(e.Scores))
                    .Select(e => e.Threshold)
                    .RunLocal((threshold, ctx) => { });
        }
        """;

    private const string StringEqualsSource = Prelude + """
        public static class StringEqualsUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.EncounterEvent>().Where(e => e.Zone.Equals(e.Zone))
                    .Select(e => e.Distance)
                    .RunLocal((distance, ctx) => { });
        }
        """;

    private const string ScalarEqualsSource = Prelude + """
        public static class ScalarEqualsUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.EncounterEvent>().Where(e => e.Distance.Equals(e.Distance))
                    .Select(e => e.Zone)
                    .RunLocal((zone, ctx) => { });
        }
        """;

    [Fact]
    public void Instance_equals_on_non_scalar_operands_is_rejected_like_double_equals()
    {
        // e.Scores.Equals(e.Scores) compares two List<int> values. C# List<T>.Equals is REFERENCE equality, but the
        // sandbox Eq compares structurally, so lowering it would silently change the predicate's meaning — exactly the
        // divergence the `==` path already rejects. The chain must fail safe (skipped), not lower to a structural
        // list comparison.
        _ = Compile(ListEqualsSource, enableInterceptors: true);
        Assert.DoesNotContain("ReadProjected", GeneratedSource(ListEqualsSource));
    }

    [Fact]
    public void Instance_equals_on_a_string_receiver_still_lowers()
    {
        // string.Equals matches C# string equality and the sandbox StringEquals, so a string-operand Equals must keep
        // lowering — the scalar-only guard must not over-reject it.
        _ = Compile(StringEqualsSource, enableInterceptors: true);
        Assert.Contains("ReadProjected", GeneratedSource(StringEqualsSource));
    }

    [Fact]
    public void Instance_equals_on_a_scalar_receiver_still_lowers()
    {
        // int.Equals(int) is value equality matching the sandbox Eq, so a scalar-operand Equals must keep lowering.
        _ = Compile(ScalarEqualsSource, enableInterceptors: true);
        Assert.Contains("ReadProjected", GeneratedSource(ScalarEqualsSource));
    }
}
