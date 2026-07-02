using DotBoxD.Kernels.Game.Server.Abstractions.Ipc;
using DotBoxD.Kernels.Game.Server.PluginCatalog;
using DotBoxD.Kernels.Game.Server.Simulation;
using DotBoxD.Plugins.Json;
using PluginServer = DotBoxD.Plugins.PluginServer;

namespace DotBoxD.Kernels.Game.Server.Ipc;

internal sealed class GameClientControlService : GamePluginControlService
{
    public GameClientControlService(
        PluginServer server,
        PluginSession session,
        GameCommandSink sink,
        GameWorld world,
        ClientOperationRegistry operations,
        string playerId,
        IPluginEventCallback? eventCallback)
        : base(server, session, sink, world, operations, playerId, eventCallback)
    {
    }

    public override async ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(packageJson);

        var package = PluginPackageJsonSerializer.Import(packageJson);
        Console.WriteLine($"[server] installing client feed hook '{package.Manifest.PluginId}'...");
        var kernel = await _session.InstallAndWireAsync(
            package,
            _kernelWiring.WireHook,
            policy: pkg => FeedPolicy.ForKernel(_server.GetRequiredCapabilities(pkg)),
            validate: _kernelWiring.ValidateRoute,
            ct).ConfigureAwait(false);
        Console.WriteLine($"[server] installed client feed hook '{kernel.Manifest.PluginId}'.");
        return InstallRouteId(kernel);
    }

    public override async ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(packageJson);

        var package = PluginPackageJsonSerializer.Import(packageJson);
        Console.WriteLine($"[server] installing client feed subscription '{package.Manifest.PluginId}'...");
        var kernel = await _session.InstallAndWireAsync(
            package,
            _kernelWiring.WireSubscription,
            policy: pkg => FeedPolicy.ForKernel(_server.GetRequiredCapabilities(pkg)),
            validate: _kernelWiring.ValidateRoute,
            ct).ConfigureAwait(false);
        EventIndexDiagnostics.Report(kernel);
        Console.WriteLine($"[server] installed client feed subscription '{kernel.Manifest.PluginId}'.");
        return InstallRouteId(kernel);
    }

    public override ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(packageJson);
        ct.ThrowIfCancellationRequested();
        const string message = "refused: server halves install from disk under the operator allow-list";
        Console.WriteLine($"[server] {message}.");
        return ValueTask.FromResult(message);
    }

    public override ValueTask<byte[]> InvokeServerExtensionAsync(
        string pluginId,
        byte[] arguments,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pluginId);
        ArgumentNullException.ThrowIfNull(arguments);
        ct.ThrowIfCancellationRequested();
        throw new InvalidOperationException("Remote server-extension invocation is disabled; use CallPluginOperationAsync.");
    }
}
