using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class RpcKernelNumericConversionGenerationTests
{
    private const string LongLiteralSource = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        [ServerExtension("long-literal")]
        public sealed partial class LongLiteralKernel
        {
            public long Zero(HookContext ctx)
            {
                return 0;
            }
        }
        """;

    private const string LongParameterSource = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        [ServerExtension("long-parameter")]
        public sealed partial class LongParameterKernel
        {
            public long Echo(int value, HookContext ctx)
            {
                return value;
            }
        }
        """;

    [Fact]
    public async Task Generated_rpc_kernel_preserves_implicit_numeric_literal_conversions()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            LongLiteralSource,
            "Sample.LongLiteralPluginPackage");

        using var server = PluginServer.Create(defaultPolicy: PurePolicy());
        var kernel = await server.InstallServerExtensionAsync(package);

        var result = await kernel.InvokeServerExtensionAsync([]);

        Assert.Equal(0L, Assert.IsType<I64Value>(result).Value);
    }

    [Fact]
    public async Task Generated_rpc_kernel_preserves_implicit_numeric_variable_conversions()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            LongParameterSource,
            "Sample.LongParameterPluginPackage");

        using var server = PluginServer.Create(defaultPolicy: PurePolicy());
        var kernel = await server.InstallServerExtensionAsync(package);

        var result = await kernel.InvokeServerExtensionAsync([SandboxValue.FromInt32(42)]);

        Assert.Equal(42L, Assert.IsType<I64Value>(result).Value);
    }

    private static SandboxPolicy PurePolicy()
        => SandboxPolicyBuilder.Create()
            .WithFuel(10_000)
            .WithMaxHostCalls(100)
            .WithWallTime(TimeSpan.FromSeconds(5))
            .Build();
}
