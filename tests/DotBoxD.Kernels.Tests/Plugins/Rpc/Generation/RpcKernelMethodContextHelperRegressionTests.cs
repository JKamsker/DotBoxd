using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class RpcKernelMethodContextHelperRegressionTests
{
    [Fact]
    public async Task ServerExtension_context_KernelMethod_can_call_sibling_context_KernelMethod()
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
                    public int AddOne(int value) => this.Add(value, 1);

                    [KernelMethod]
                    public int Add(int value, int delta) => value + delta;
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
                [ServerExtension("context-kernel-helper-chain")]
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
