using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class InvokeAsyncGeneratedReceiverSurpriseTests
{
    [Fact]
    public void Explicit_capture_bag_sync_out_local_avoids_user_local_collision()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            public sealed class Capture
            {
                public string MonsterId { get; set; } = "";
                public int LastHealth { get; set; }
            }

            public static ValueTask<int> Run(RemotePluginServer kernels, Capture captures)
                => kernels.InvokeAsync(captures, async (IGameWorldAccess world, Capture bag) =>
                {
                    bag.LastHealth = world.GetHealth(bag.MonsterId);
                    var __syncOut_LastHealth = 42;
                    return bag.LastHealth + __syncOut_LastHealth;
                });
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("__syncOut_LastHealth_0", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Implicit_captured_collection_transitive_alias_mutation_reports_InvokeAsync_diagnostic()
    {
        var result = RunGenerator(UsageSource("""
            public static ValueTask<int> Run(RemotePluginServer kernels, System.Collections.Generic.List<int> values)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    System.Collections.Generic.List<int> alias = [];
                    alias = values;
                    var transitive = alias;
                    transitive.Add(world.GetHealth("monster-1"));
                    return transitive.Count;
                });
            """));

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("captured collection 'values'", StringComparison.Ordinal));
    }

    [Fact]
    public void Generated_receiver_fallback_honors_explicit_generic_return_type()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            public static ValueTask<long> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync<long>(async (IGameWorldAccess world) =>
                {
                    return world.GetHealth("monster-1");
                });
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("\\\"returnType\\\":\\\"I64\\\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_receiver_capture_fallback_honors_explicit_generic_return_type()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            public sealed class Capture
            {
                public string MonsterId { get; set; } = "";
                public int LastHealth { get; set; }
            }

            public static ValueTask<long> Run(RemotePluginServer kernels, Capture captures)
                => kernels.InvokeAsync<Capture, long>(captures, async (IGameWorldAccess world, Capture bag) =>
                {
                    bag.LastHealth = world.GetHealth(bag.MonsterId);
                    return bag.LastHealth;
                });
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("\\\"returnType\\\":{\\\"name\\\":\\\"Record\\\",\\\"arguments\\\":[\\\"I64\\\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Unqualified_InvokeAsync_inside_generated_facade_is_lowered()
    {
        var result = RunGeneratorAndAssertCompiles(GeneratedFacadeBodySource("""
                public ValueTask<int> Probe()
                    => InvokeAsync(async (IGameWorldAccess world) =>
                    {
                        return world.GetHealth("monster-1");
                    });
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("AnonymousInvokeAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_receiver_lowers_simple_block_lambda_InvokeAsync()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            public static ValueTask<int> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async world =>
                {
                    return world.GetHealth("monster-1");
                });
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("AnonymousInvokeAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Actual_unrelated_server_interface_is_not_treated_as_generated_receiver()
    {
        var result = RunGeneratorAndAssertCompiles(GeneratedFacadeBodySource("""
                public ValueTask<int> Probe(IGameServer server)
                    => server.InvokeAsync(async (IGameWorldAccess world) =>
                    {
                        return world.GetHealth("monster-1");
                    });
            """, """
            public interface IGameServer
            {
                ValueTask<int> InvokeAsync(Func<IGameWorldAccess, ValueTask<int>> lambda);
            }
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.DoesNotContain("AnonymousInvokeAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Lookalike_IPluginServer_interface_without_generated_shape_reports_DBXK100()
    {
        var result = RunGenerator(GeneratedFacadeBodySource("""
                public ValueTask<int> Probe(IGameWorldServer server)
                    => server.InvokeAsync(async (IGameWorldAccess world) =>
                    {
                        return world.GetHealth("monster-1");
                    });
            """, """
            public interface IGameWorldServer : IPluginServer<IGameWorldAccess>;
            """));

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Id == "DBXK100");
        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("AnonymousInvokeAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void Explicit_capture_bag_uses_explicit_generic_upcast_type()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            public class CaptureBase
            {
                public string MonsterId { get; set; } = "";
                public int LastHealth { get; set; }
            }

            public sealed class DerivedCapture : CaptureBase;

            public static ValueTask<int> Run(RemotePluginServer kernels, DerivedCapture captures)
                => kernels.InvokeAsync<CaptureBase, int>(captures, async (IGameWorldAccess world, CaptureBase bag) =>
                {
                    bag.LastHealth = world.GetHealth(bag.MonsterId);
                    return bag.LastHealth;
                });
            """));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("CaptureBase", StringComparison.Ordinal));
    }

    [Fact]
    public void Explicit_capture_bag_alias_syncs_member_assignments()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            public sealed class Capture
            {
                public string MonsterId { get; set; } = "";
                public int LastHealth { get; set; }
            }

            public static ValueTask<int> Run(RemotePluginServer kernels, Capture captures)
                => kernels.InvokeAsync(captures, async (IGameWorldAccess world, Capture bag) =>
                {
                    var alias = bag;
                    alias.LastHealth = world.GetHealth(alias.MonsterId);
                    return alias.LastHealth;
                });
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("captures.LastHealth =", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_capture_bag_alias_mutable_collection_mutation_reports_DBXK100()
    {
        var result = RunGenerator(UsageSource("""
            public sealed class Capture
            {
                public System.Collections.Generic.List<int> Values { get; set; } = [];
            }

            public static ValueTask<int> Run(RemotePluginServer kernels, Capture captures)
                => kernels.InvokeAsync(captures, async (IGameWorldAccess world, Capture bag) =>
                {
                    var alias = bag;
                    var values = alias.Values;
                    values.Add(world.GetHealth("monster-1"));
                    return values.Count;
                });
            """));

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("captured collection 'Values'", StringComparison.Ordinal));
    }

    [Fact]
    public void Unqualified_InvokeAsync_inside_generated_facade_respects_user_overload()
    {
        var result = RunGeneratorAndAssertCompiles(GeneratedFacadeBodySource("""
                private ValueTask<int> InvokeAsync(Func<IGameWorldAccess, ValueTask<int>> lambda)
                    => new(7);

                public ValueTask<int> Probe()
                    => InvokeAsync(async (IGameWorldAccess world) =>
                    {
                        return world.GetHealth("monster-1");
                    });
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.DoesNotContain("AnonymousInvokeAsync", source, StringComparison.Ordinal);
    }

}
