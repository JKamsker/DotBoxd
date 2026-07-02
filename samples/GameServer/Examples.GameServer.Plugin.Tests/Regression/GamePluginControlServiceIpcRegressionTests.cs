using DotBoxD.Kernels.Game.Server.Abstractions.Ipc;
using DotBoxD.Plugins.Json;
using DotBoxD.Pushdown.Services;

namespace DotBoxD.Kernels.Game.Plugin.Tests.Regression;

public sealed class GamePluginControlServiceIpcRegressionTests
{
    [Fact]
    public async Task Inherited_server_extension_wire_method_round_trips_over_generated_ipc()
    {
        var pipeName = "dotboxd-control-ipc-" + Guid.NewGuid().ToString("N");
        var control = new EchoControlService();
        await using var host = RpcMessagePackIpc.ListenNamedPipe(pipeName, peer =>
        {
            global::DotBoxD.Services.Generated.DotBoxDGeneratedExtensions.ProvideGamePluginControlService(
                peer,
                control);
        });
        await host.StartAsync();
        await using var connection = await RpcMessagePackIpc.ConnectNamedPipeAsync(pipeName);
        var proxy = connection.Get<IGamePluginControlService>();

        var response = await proxy.InvokeServerExtensionAsync("monster-killer", [1, 2, 3]);

        Assert.Equal("monster-killer", control.LastPluginId);
        Assert.Equal([1, 2, 3], control.LastArguments);
        Assert.Equal([3, 2, 1], response);
    }

    private sealed class EchoControlService : IGamePluginControlService
    {
        public string? LastPluginId { get; private set; }
        public byte[] LastArguments { get; private set; } = [];

        public ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default)
            => ValueTask.FromResult(RouteId(packageJson));

        public ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default)
            => ValueTask.FromResult(RouteId(packageJson));

        public ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default)
            => ValueTask.FromResult(PluginPackageJsonSerializer.Import(packageJson).Manifest.PluginId);

        public ValueTask<byte[]> InvokeServerExtensionAsync(
            string pluginId,
            byte[] arguments,
            CancellationToken ct = default)
        {
            LastPluginId = pluginId;
            LastArguments = arguments;
            return ValueTask.FromResult(arguments.Reverse().ToArray());
        }

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

        private static string RouteId(string packageJson)
        {
            var package = PluginPackageJsonSerializer.Import(packageJson);
            return package.CallbackSubscriptionId ?? package.Manifest.PluginId;
        }
    }
}
