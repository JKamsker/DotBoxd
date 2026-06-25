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

    [Fact]
    public async Task ServerExtension_inlines_static_extension_KernelMethod_helper_with_named_default_arguments()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            """
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            [ServerExtension("kernel-method-extension-helper")]
            public sealed partial class HelperKernel
            {
                public int Run(int value, HookContext ctx)
                {
                    return value.Add(delta: 1);
                }
            }

            public static class HelperMethods
            {
                [KernelMethod]
                public static int Add(this int value, int delta = 1) => value + delta;
            }
            """,
            "Sample.HelperPluginPackage");

        using var server = PluginServer.Create(defaultPolicy: PurePolicy());
        var kernel = await server.InstallServerExtensionAsync(package);
        var result = await kernel.InvokeServerExtensionAsync([SandboxValue.FromInt32(41)]);

        Assert.Equal(42, Assert.IsType<I32Value>(result).Value);
    }

    [Fact]
    public async Task ServerExtension_inlines_generated_context_instance_KernelMethod_helper()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;
            using DotBoxD.Services.Attributes;

            namespace Sample
            {
                [DotBoxDService]
                public interface IGameWorld;

                [GeneratePluginServer(Context = typeof(GamePluginContext))]
                public partial class RemotePluginServer : IGameWorld;

                public sealed partial class GamePluginContext
                {
                    [KernelMethod]
                    public int AddOne(int value) => value + 1;
                }
            }

            namespace Sample.Ipc
            {
                public readonly record struct LiveSettingUpdate(string Name, string Value);

                public interface IGamePluginControlService : DotBoxD.Plugins.IServerExtensionWireClient
                {
                    ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default);
                    ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default);
                    ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default);
                    ValueTask UpdateSettingsAsync(
                        string pluginId,
                        LiveSettingUpdate[] updates,
                        bool atomic = false,
                        CancellationToken ct = default);
                    ValueTask HoldUntilShutdownAsync(CancellationToken ct = default);
                }
            }

            namespace DotBoxD.Services.Generated
            {
                public static class DotBoxDGeneratedExtensions
                {
                    public static Sample.IGameWorld GetGameWorld(DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new System.InvalidOperationException("not used");
                }
            }

            namespace Sample
            {
                [ServerExtension("context-kernel-helper")]
                public sealed partial class ContextHelperKernel
                {
                    public int Run(int value, GamePluginContext ctx)
                    {
                        return ctx.AddOne(value);
                    }
                }
            }
            """,
            "Sample.ContextHelperPluginPackage");

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
