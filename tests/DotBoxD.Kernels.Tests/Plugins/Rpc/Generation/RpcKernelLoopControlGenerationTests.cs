using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

/// <summary>
/// Regression guard for issue #68: <c>continue</c> and <c>break</c> inside a
/// <c>[ServerExtension]</c> loop are accepted by the analyzer and lowered to the kernel IR's
/// structured loop control, instead of being rejected with DBXK100.
/// </summary>
public sealed class RpcKernelLoopControlGenerationTests
{
    private const string SumPositivesContinueSource = """
        using System.Collections.Generic;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        [ServerExtension("loop-continue")]
        public sealed partial class LoopContinueKernel
        {
            public int SumPositives(List<int> values, HookContext ctx)
            {
                var total = 0;
                foreach (var v in values)
                {
                    if (v <= 0)
                        continue;
                    total += v;
                }
                return total;
            }
        }
        """;

    private const string SumUntilNegativeBreakSource = """
        using System.Collections.Generic;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        [ServerExtension("loop-break")]
        public sealed partial class LoopBreakKernel
        {
            public int SumUntilNegative(List<int> values, HookContext ctx)
            {
                var total = 0;
                foreach (var v in values)
                {
                    if (v < 0)
                        break;
                    total += v;
                }
                return total;
            }
        }
        """;

    [Fact]
    public void Continue_inside_a_server_extension_loop_is_accepted()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(SumPositivesContinueSource);

        Assert.DoesNotContain(diagnostics, d => d.Id == "DBXK100");
    }

    [Fact]
    public void Break_inside_a_server_extension_loop_is_accepted()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(SumUntilNegativeBreakSource);

        Assert.DoesNotContain(diagnostics, d => d.Id == "DBXK100");
    }

    [Fact]
    public async Task Continue_skips_iterations_at_runtime()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            SumPositivesContinueSource,
            "Sample.LoopContinuePluginPackage");

        using var server = PluginServer.Create(defaultPolicy: PurePolicy());
        var kernel = await server.InstallServerExtensionAsync(package);

        var values = SandboxValue.FromList(
            [SandboxValue.FromInt32(2), SandboxValue.FromInt32(-5), SandboxValue.FromInt32(0), SandboxValue.FromInt32(4)],
            SandboxType.I32);
        var result = await kernel.InvokeServerExtensionAsync([values]);

        // 2 + 4, skipping -5 and 0.
        Assert.Equal(6, Assert.IsType<I32Value>(result).Value);
    }

    [Fact]
    public async Task Break_exits_the_loop_at_runtime()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            SumUntilNegativeBreakSource,
            "Sample.LoopBreakPluginPackage");

        using var server = PluginServer.Create(defaultPolicy: PurePolicy());
        var kernel = await server.InstallServerExtensionAsync(package);

        var values = SandboxValue.FromList(
            [SandboxValue.FromInt32(3), SandboxValue.FromInt32(4), SandboxValue.FromInt32(-1), SandboxValue.FromInt32(9)],
            SandboxType.I32);
        var result = await kernel.InvokeServerExtensionAsync([values]);

        // 3 + 4, stops at -1.
        Assert.Equal(7, Assert.IsType<I32Value>(result).Value);
    }

    private static SandboxPolicy PurePolicy()
        => SandboxPolicyBuilder.Create()
            .WithFuel(10_000)
            .WithMaxHostCalls(100)
            .WithWallTime(TimeSpan.FromSeconds(5))
            .Build();
}
