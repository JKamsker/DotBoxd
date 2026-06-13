using SafeIR;

namespace SafeIR.Tests;

/// <summary>
/// Regression coverage for PAL-0029: the generic
/// <see cref="SandboxPolicyBuilder.Grant(string, object)"/> extensibility overload rediscovered
/// parameter object metadata via <c>GetType().GetProperties(...)</c>, re-filtered public getters
/// and indexers, and invoked a reflection <c>GetValue</c> for every grant -- even when many grants
/// reuse the same anonymous/options parameter type. The fix caches the readable property accessors
/// per runtime <see cref="System.Type"/> so policy setup pays that reflection metadata work once
/// per parameter shape instead of once per grant, while preserving the exact parameter snapshot
/// (key selection, key order, invariant string conversion) and the per-grant immutable copy.
///
/// These tests pin both the preserved observable behavior and the steady-state per-grant
/// allocation: building grants that reuse one parameter type must not allocate the reflected
/// property-metadata arrays on every grant. The behavioral asserts use only public API; the
/// allocation assert compares the reused-shape per-grant cost against the irreducible dictionary
/// snapshot cost of the fast IReadOnlyDictionary path.
/// </summary>
public sealed class Fix_PAL_0029_Tests
{
    private const int WarmupGrants = 16;
    private const int MeasuredGrants = 4_000;

    [Fact]
    public void Reused_parameter_shape_produces_identical_parameter_snapshot_each_grant()
    {
        var first = BuildSingleGrant(new CustomCapabilityParameters("/data", 4096, true));
        var second = BuildSingleGrant(new CustomCapabilityParameters("/logs", 8192, false));

        // Same readable keys in the same order regardless of how many times the shape is reused.
        Assert.Equal(new[] { "Root", "MaxBytes", "Enabled" }, first.Parameters.Keys.ToArray());
        Assert.Equal(new[] { "Root", "MaxBytes", "Enabled" }, second.Parameters.Keys.ToArray());

        Assert.Equal("/data", first.Parameters["Root"]);
        Assert.Equal("4096", first.Parameters["MaxBytes"]);
        Assert.Equal("True", first.Parameters["Enabled"]);

        Assert.Equal("/logs", second.Parameters["Root"]);
        Assert.Equal("8192", second.Parameters["MaxBytes"]);
        Assert.Equal("False", second.Parameters["Enabled"]);
    }

    [Fact]
    public void Object_grant_keeps_only_public_instance_non_indexer_getters()
    {
        var grant = BuildSingleGrant(new ShapeWithExcludedMembers());

        var parameter = Assert.Single(grant.Parameters);
        Assert.Equal("PublicValue", parameter.Key);
        Assert.Equal("visible", parameter.Value);
    }

    [Fact]
    public void Direct_dictionary_parameters_still_use_the_fast_copy_path()
    {
        var source = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["root"] = "/data",
            ["maxBytes"] = "4096"
        };

        var grant = BuildSingleGrant(source);

        Assert.Equal("/data", grant.Parameters["root"]);
        Assert.Equal("4096", grant.Parameters["maxBytes"]);

        // The snapshot must be an independent copy, not the caller's mutable instance.
        source["root"] = "/mutated";
        Assert.Equal("/data", grant.Parameters["root"]);
    }

    [Fact]
    public void Reusing_a_parameter_shape_does_not_reflect_metadata_per_grant()
    {
        var objectShapeCost = PerGrantAllocation(static () =>
            SandboxPolicyBuilder.Create()
                .Grant("custom.capability", new CustomCapabilityParameters("/data", 4096, true))
                .Build());

        // Lower bound: a grant whose parameters already implement IReadOnlyDictionary skips all
        // reflection and only pays the mandatory per-grant dictionary snapshot. The cached object
        // path may legitimately cost a little more (boxed value conversion strings), but it must
        // stay in the same allocation class -- NOT the per-grant GetProperties metadata array plus
        // reflection invoke boxing that the bug allocated for every grant.
        var dictionaryShapeCost = PerGrantAllocation(static () =>
            SandboxPolicyBuilder.Create()
                .Grant(
                    "custom.capability",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["Root"] = "/data",
                        ["MaxBytes"] = "4096",
                        ["Enabled"] = "True"
                    })
                .Build());

        Assert.True(
            objectShapeCost <= dictionaryShapeCost * 3,
            $"Reused object parameter shape allocated ~{objectShapeCost} bytes/grant versus " +
            $"~{dictionaryShapeCost} bytes/grant for the dictionary fast path. Per-grant reflection " +
            "metadata enumeration was expected to be cached away after PAL-0029.");
    }

    private static CapabilityGrant BuildSingleGrant(object parameters)
    {
        var policy = SandboxPolicyBuilder.Create()
            .Grant("custom.capability", parameters)
            .Build();
        return Assert.Single(policy.Grants);
    }

    private static long PerGrantAllocation(Action buildPolicy)
    {
        // Warm up: prime the per-type accessor cache and JIT before measuring steady state.
        for (var i = 0; i < WarmupGrants; i++)
        {
            buildPolicy();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < MeasuredGrants; i++)
        {
            buildPolicy();
        }

        return (GC.GetAllocatedBytesForCurrentThread() - before) / MeasuredGrants;
    }

    private sealed record CustomCapabilityParameters(string Root, int MaxBytes, bool Enabled);

    private sealed class ShapeWithExcludedMembers
    {
        public static string StaticValue => "static";

        public string PublicValue => "visible";

        public string PrivateGetter { private get; set; } = "hidden";

        public string this[int index] => "indexed";
    }
}
