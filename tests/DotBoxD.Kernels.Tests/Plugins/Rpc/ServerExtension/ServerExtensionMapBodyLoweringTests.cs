using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

/// <summary>
/// Server-side map body lowering (issue #44): a generated <c>[ServerExtension]</c> kernel reads a map
/// (<c>map.get</c>/<c>map.containsKey</c>) and builds one (<c>map.empty</c>/<c>map.set</c>), installed and
/// invoked end to end. Also pins the scalar-only map-key policy. The wire/marshaller/client round-trips are
/// covered in <see cref="ServerExtensionMapTypeSupportTests"/>.
/// </summary>
public sealed class ServerExtensionMapBodyLoweringTests
{
    [Fact]
    public async Task A_generated_kernel_reads_and_builds_a_map_server_side()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(MapBodySource, "Sample.ScoreBumpPluginPackage");
        Assert.Equal("Bump", package.Manifest.RpcEntrypoint);

        using var server = PluginServer.Create(defaultPolicy: PurePolicy());
        var kernel = await server.InstallServerExtensionAsync(package);

        var scores = SandboxValue.FromMap(
            new Dictionary<SandboxValue, SandboxValue>
            {
                [SandboxValue.FromString("hero")] = SandboxValue.FromInt32(41)
            },
            SandboxType.String,
            SandboxType.I32);

        var hit = await kernel.InvokeServerExtensionAsync([scores, SandboxValue.FromString("hero")]);

        var bumped = Assert.IsType<MapValue>(hit);
        Assert.Equal(42, Assert.IsType<I32Value>(bumped.Values[SandboxValue.FromString("hero")]).Value);
        Assert.Single(bumped.Values);

        var miss = await kernel.InvokeServerExtensionAsync([scores, SandboxValue.FromString("villain")]);
        Assert.Empty(Assert.IsType<MapValue>(miss).Values);
    }

    [Fact]
    public async Task A_generated_kernel_overwrites_an_existing_map_key_server_side()
    {
        // Exercises map.set's replace branch: the body writes a key, then writes the same key again in the
        // same map (dict[key] = first; dict[key] = second). The build-into-a-fresh-map cases only ever insert.
        var package = PluginAnalyzerGeneratedPackageFactory.Create(MapReplaceBodySource, "Sample.ScoreReplacePluginPackage");

        using var server = PluginServer.Create(defaultPolicy: PurePolicy());
        var kernel = await server.InstallServerExtensionAsync(package);

        var result = await kernel.InvokeServerExtensionAsync(
            [SandboxValue.FromString("hero"), SandboxValue.FromInt32(1), SandboxValue.FromInt32(9)]);

        var map = Assert.IsType<MapValue>(result);
        Assert.Equal(9, Assert.IsType<I32Value>(map.Values[SandboxValue.FromString("hero")]).Value);
        Assert.Single(map.Values);
    }

    [Fact]
    public void Server_extension_rejects_an_unsupported_map_key_type()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using System.Collections.Generic;
            using DotBoxD.Kernels;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            [ServerExtension("bad-map-key")]
            public sealed partial class BadMapKeyKernel
            {
                public int Use(Dictionary<double, int> scores, HookContext ctx)
                {
                    return 0;
                }
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" && d.GetMessage().Contains("map key", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Server_extension_rejects_map_comparer_constructors()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using System;
            using System.Collections.Generic;
            using DotBoxD.Kernels;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            [ServerExtension("score-comparer")]
            public sealed partial class ScoreComparerKernel
            {
                public Dictionary<string, int> Build(HookContext ctx)
                {
                    var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    result["hero"] = 1;
                    result["HERO"] = 2;
                    return result;
                }
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("map", StringComparison.OrdinalIgnoreCase) &&
                 d.GetMessage().Contains("comparer", StringComparison.OrdinalIgnoreCase));
    }

    private static SandboxPolicy PurePolicy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .WithFuel(100_000)
            .WithMaxHostCalls(10_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();

    private const string MapBodySource = """
        using System.Collections.Generic;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        [ServerExtension("score-bump")]
        public sealed partial class ScoreBumpKernel
        {
            public Dictionary<string, int> Bump(Dictionary<string, int> scores, string key, HookContext ctx)
            {
                Dictionary<string, int> result = new();
                if (scores.ContainsKey(key))
                {
                    result[key] = scores[key] + 1;
                }

                return result;
            }
        }
        """;

    private const string MapReplaceBodySource = """
        using System.Collections.Generic;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        [ServerExtension("score-replace")]
        public sealed partial class ScoreReplaceKernel
        {
            public Dictionary<string, int> SetTwice(string key, int first, int second, HookContext ctx)
            {
                var result = new Dictionary<string, int>();
                result[key] = first;
                result[key] = second;
                return result;
            }
        }
        """;
}
