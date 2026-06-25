using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class RpcKernelMethodGenerationTests
{
    [Fact]
    public async Task ServerExtension_inlines_static_KernelMethod_helper()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            """
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            [ServerExtension("kernel-method-helper")]
            public sealed partial class HelperKernel
            {
                public int Run(int value, HookContext ctx)
                {
                    return AddOne(value);
                }

                [KernelMethod]
                public static int AddOne(int value) => value + 1;
            }
            """,
            "Sample.HelperPluginPackage");

        using var server = PluginServer.Create(defaultPolicy: PurePolicy());
        var kernel = await server.InstallServerExtensionAsync(package);
        var result = await kernel.InvokeServerExtensionAsync([SandboxValue.FromInt32(41)]);

        Assert.Equal(42, Assert.IsType<I32Value>(result).Value);
    }

    private static SandboxPolicy PurePolicy()
        => SandboxPolicyBuilder.Create()
            .WithFuel(10_000)
            .WithMaxHostCalls(100)
            .WithWallTime(TimeSpan.FromSeconds(5))
            .Build();
}
