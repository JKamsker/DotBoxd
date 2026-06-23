using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class RpcKernelForeachGenerationTests
{
    private const string UserTempCollisionSource = """
        using System.Collections.Generic;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        [ServerExtension("foreach-collision")]
        public sealed partial class ForeachCollisionKernel
        {
            public int SumWithLocal(List<int> values, HookContext ctx)
            {
                var __sir_i0 = 100;
                var total = 0;
                foreach (var value in values)
                {
                    total += value;
                }

                return __sir_i0 + total;
            }
        }
        """;

    [Fact]
    public async Task Generated_rpc_foreach_temps_do_not_collide_with_user_locals()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            UserTempCollisionSource,
            "Sample.ForeachCollisionPluginPackage");

        using var server = PluginServer.Create(defaultPolicy: PurePolicy());
        var kernel = await server.InstallServerExtensionAsync(package);
        var values = SandboxValue.FromList(
            [SandboxValue.FromInt32(1), SandboxValue.FromInt32(2), SandboxValue.FromInt32(3)],
            SandboxType.I32);

        var result = await kernel.InvokeServerExtensionAsync([values]);

        Assert.Equal(106, Assert.IsType<I32Value>(result).Value);
    }

    [Fact]
    public void Kernel_rpc_service_rejects_foreach_over_non_list_source()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            [ServerExtension("foreach-string")]
            public sealed partial class ForeachStringKernel
            {
                public int CountName(string name, HookContext ctx)
                {
                    var count = 0;
                    foreach (var ch in name)
                    {
                        count++;
                    }

                    return count;
                }
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("foreach source 'name' must be a supported list type", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Generated_rpc_foreach_preserves_iteration_variable_numeric_conversion()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            """
            using System.Collections.Generic;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            [ServerExtension("foreach-conversion")]
            public sealed partial class ForeachConversionKernel
            {
                public long Sum(List<int> values, HookContext ctx)
                {
                    long total = 0;
                    foreach (long value in values)
                    {
                        total += value;
                    }

                    return total;
                }
            }
            """,
            "Sample.ForeachConversionPluginPackage");

        using var server = PluginServer.Create(defaultPolicy: PurePolicy());
        var kernel = await server.InstallServerExtensionAsync(package);
        var values = SandboxValue.FromList(
            [SandboxValue.FromInt32(1), SandboxValue.FromInt32(2), SandboxValue.FromInt32(3)],
            SandboxType.I32);

        var result = await kernel.InvokeServerExtensionAsync([values]);

        Assert.Equal(6L, Assert.IsType<I64Value>(result).Value);
    }

    private static SandboxPolicy PurePolicy()
        => SandboxPolicyBuilder.Create()
            .WithFuel(10_000)
            .WithMaxHostCalls(100)
            .WithWallTime(TimeSpan.FromSeconds(5))
            .Build();
}
