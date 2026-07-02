using DotBoxD.Kernels.Game.Plugin.Authoring;
using DotBoxD.Kernels.Game.Server.Abstractions.Ipc;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Json;
using DotBoxD.Pushdown.Services;
using DotBoxD.Services.Peer;

namespace DotBoxD.Kernels.Game.Plugin.Tests;

/// <summary>
/// End-to-end proof that the GENERATED <c>[GeneratePluginServer]</c> facade — not hand-wired plumbing — makes a
/// remote <c>RunLocal</c> lambda fire in the plugin process over a REAL named pipe. The facade now owns a
/// <c>RemoteLocalHandlerRegistry</c>, threads it into <c>server.Hooks</c>, and provides the reverse
/// <c>IPluginEventCallback</c> sink on the peer at <c>StartAsync</c>, so a server push reaches the native
/// delegate. Before this wiring the same chain threw <c>LocalHandlersNotSupported</c> at install time.
/// </summary>
/// <remarks>
/// The server-side filter/projection (lowered <c>Where</c>/<c>Select</c> IR) is proven by
/// <see cref="RemoteRunLocalIpcPremiseTests"/>; here the per-event push is simulated with a hand-encoded
/// projection so the test isolates the facade's reverse path — registry ownership, sink provisioning, and
/// decode-into-delegate — rather than re-proving server-side execution.
/// </remarks>
public sealed class RemoteRunLocalFacadeIpcTests
{
    [Fact]
    public async Task Generated_facade_provides_the_callback_so_a_remote_RunLocal_lambda_fires_over_ipc()
    {
        var pipeName = "dotboxd-runlocal-facade-" + Guid.NewGuid().ToString("N");
        var serverPeerReady = new TaskCompletionSource<RpcPeer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var control = new InstallCapturingControlService();

        // Server side: a minimal control plane provided on the peer when the plugin connects. No world impl is
        // provided — the facade's world proxy getters are lazy and this test never calls a world method.
        await using var ipcHost = RpcMessagePackIpc.ListenNamedPipe(pipeName, peer =>
        {
            global::DotBoxD.Services.Generated.DotBoxDGeneratedExtensions.ProvideGamePluginControlService(
                peer,
                control);
            serverPeerReady.TrySetResult(peer);
        });
        await ipcHost.StartAsync();

        // Plugin side: the one-line generated facade. StartAsync connects and (new behavior) provides the
        // reverse event-callback sink on the peer so the server can push to native RunLocal terminals.
        await using IGameWorldServer server = GamePluginServerBuilder.FromPipeName(pipeName).Build();
        await server.StartAsync();

        // The user's exact scenario: a remote RunLocal chain on the runtime hook surface. It installs through the
        // control plane (returning the subscription id) and registers the native delegate in the facade's own
        // RemoteLocalHandlerRegistry — no hand-wiring.
        var calmedOnPluginSide = new List<string>();
        LocalReactions.ConfigureCalmReaction(server.Hooks, monsterId =>
        {
            lock (calmedOnPluginSide)
            {
                calmedOnPluginSide.Add(monsterId);
            }
        });

        var subscriptionId = await control.Installed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Simulate the server pushing one filtered+projected MonsterId back over the pipe to the plugin's
        // generated sink.
        var serverPeer = await serverPeerReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var pushProxy =
            global::DotBoxD.Services.Generated.DotBoxDGeneratedExtensions.GetPluginEventCallback(serverPeer);
        await pushProxy.OnEventAsync(subscriptionId, KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.String("monster-7")));

        // The native RunLocal delegate ran in the plugin process with the decoded projection — reachable only
        // because the generated facade owns the registry and provided the sink.
        lock (calmedOnPluginSide)
        {
            Assert.Equal(["monster-7"], calmedOnPluginSide);
        }
    }

    // Minimal control plane: captures the installed subscription id so the test can push to it. A real host also
    // runs the lowered IR and pushes per matching event (see GamePluginControlService).
    private sealed class InstallCapturingControlService : IGamePluginControlService
    {
        public TaskCompletionSource<string> Installed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default)
        {
            var package = PluginPackageJsonSerializer.Import(packageJson);
            var routeId = package.CallbackSubscriptionId ?? package.Manifest.PluginId;
            Installed.TrySetResult(routeId);
            return ValueTask.FromResult(routeId);
        }

        public ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default)
        {
            var package = PluginPackageJsonSerializer.Import(packageJson);
            var routeId = package.CallbackSubscriptionId ?? package.Manifest.PluginId;
            Installed.TrySetResult(routeId);
            return ValueTask.FromResult(routeId);
        }

        public ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default)
            => ValueTask.FromResult(PluginPackageJsonSerializer.Import(packageJson).Manifest.PluginId);

        public ValueTask<byte[]> InvokeServerExtensionAsync(
            string pluginId,
            byte[] arguments,
            CancellationToken ct = default)
            => ValueTask.FromResult(Array.Empty<byte>());

        public ValueTask<string> CallPluginOperationAsync(
            string operation,
            string payload,
            CancellationToken ct = default)
            => ValueTask.FromResult("denied:test-operation");

        public ValueTask UpdateSettingsAsync(
            string pluginId,
            LiveSettingUpdate[] updates,
            bool atomic = false,
            CancellationToken ct = default)
            => ValueTask.CompletedTask;

        public ValueTask HoldUntilShutdownAsync(CancellationToken ct = default) => ValueTask.CompletedTask;

        public ValueTask<WorldSnapshot> GetWorldAsync(CancellationToken ct = default)
            => ValueTask.FromResult(new WorldSnapshot([], tick: 0));

        public ValueTask<string[]> DrainEffectsAsync(CancellationToken ct = default)
            => ValueTask.FromResult(Array.Empty<string>());
    }
}
