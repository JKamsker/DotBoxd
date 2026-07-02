using DotBoxD.Kernels.Game.Server.Ipc;
using DotBoxD.Kernels.Game.Server.Simulation;
using PluginServer = DotBoxD.Plugins.PluginServer;

namespace DotBoxD.Kernels.Game.Server.PluginCatalog;

internal sealed class PluginCatalogInstaller
{
    private readonly OperatorAllowList _allowList = new();
    private readonly PluginServer _server;
    private readonly GamePluginKernelWiring _wiring;
    private readonly ClientOperationRegistry _operations;

    public PluginCatalogInstaller(PluginServer server, GameWorld world, ClientOperationRegistry operations)
    {
        _server = server;
        _wiring = new GamePluginKernelWiring(server, world, eventCallback: null);
        _operations = operations;
    }

    public async ValueTask InstallServerPartsAsync(string pluginsRoot, CancellationToken ct = default)
    {
        foreach (var part in PluginBundleLoader.LoadServerParts(pluginsRoot))
        {
            var required = _server.GetRequiredCapabilities(part.Package);
            Console.WriteLine($"[server] bundle {part.BundleId}/{part.Kind}: caps [{string.Join(", ", required)}]");
            if (!_allowList.AllowsCapabilities(part.BundleId, required))
            {
                Console.WriteLine($"[server] DENY {part.BundleId}: operator allow-list does not grant required caps.");
                continue;
            }

            await InstallPartAsync(part, required, ct).ConfigureAwait(false);
        }

        Console.WriteLine($"[server] client-callable operations: {string.Join(", ", _operations.Snapshot())}");
    }

    private async ValueTask InstallPartAsync(
        PluginBundlePart part,
        IReadOnlyList<string> required,
        CancellationToken ct)
    {
        var policy = ServerPolicy.ForKernel(required);
        switch (part.Kind)
        {
            case BundlePartKind.Hook:
                _wiring.ValidateRoute(part.Package);
                _wiring.WireHook(await _server.InstallAsync(part.Package, policy, ct).ConfigureAwait(false));
                break;
            case BundlePartKind.Subscription:
                _wiring.ValidateRoute(part.Package);
                var subscription = await _server.InstallAsync(part.Package, policy, ct).ConfigureAwait(false);
                _wiring.WireSubscription(subscription);
                EventIndexDiagnostics.Report(subscription);
                break;
            case BundlePartKind.Extension:
                var extension = await _server.InstallServerExtensionAsync(part.Package, policy, ct).ConfigureAwait(false);
                foreach (var operation in _allowList.Operations(part.BundleId))
                {
                    if (_allowList.AllowsOperation(part.BundleId, operation) &&
                        string.Equals(extension.Manifest.PluginId, operation, StringComparison.Ordinal))
                    {
                        _operations.Register(operation, extension);
                    }
                }

                break;
        }

        Console.WriteLine($"[server] ALLOW {part.BundleId}: installed {part.Package.Manifest.PluginId}.");
    }
}
