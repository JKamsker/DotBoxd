using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class RpcKernelUnaryGenerationTests
{
    private const string LogicalNotSource = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        [ServerExtension("logical-not")]
        public sealed partial class LogicalNotKernel
        {
            public bool Flip(bool value, HookContext ctx)
            {
                return !value;
            }
        }
        """;

    [Fact]
    public async Task Generated_rpc_kernel_lowers_unary_expressions_to_valid_json()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            LogicalNotSource,
            "Sample.LogicalNotPluginPackage");

        using var server = PluginServer.Create(defaultPolicy: PurePolicy());
        var kernel = await server.InstallServerExtensionAsync(package);

        var result = await kernel.InvokeServerExtensionAsync([SandboxValue.FromBool(true)]);

        Assert.False(Assert.IsType<BoolValue>(result).Value);
    }

    private static SandboxPolicy PurePolicy()
        => SandboxPolicyBuilder.Create()
            .WithFuel(10_000)
            .WithMaxHostCalls(100)
            .WithWallTime(TimeSpan.FromSeconds(5))
            .Build();
}
