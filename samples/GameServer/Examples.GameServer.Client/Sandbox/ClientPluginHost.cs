using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Game.Client.Abstractions.Events;
using DotBoxD.Kernels.Game.Client.Plugins;
using DotBoxD.Kernels.Game.Client.Rendering;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Kernel;
using PluginServer = DotBoxD.Plugins.PluginServer;

namespace DotBoxD.Kernels.Game.Client.Sandbox;

internal sealed class ClientPluginHost : IDisposable
{
    private readonly PluginServer _server;
    private InstalledKernel? _claimKernel;

    public ClientPluginHost(ConsoleHudRenderer renderer, string pluginsRoot, IGameClientControlService control)
    {
        Renderer = renderer;
        PluginsRoot = pluginsRoot;
        _server = PluginServer.Create(
            new HudMessageSink(renderer, pluginsRoot),
            configureHost: builder => builder.AddBindingsFrom<IGameClientAccess>(
                new GameClientAccess(renderer, control)),
            defaultPolicy: ClientPolicy.ForKernel([]),
            executionMode: ExecutionMode.Compiled);
        _server.Events.Resolve<ClientMonsterKilledEvent>();
        _server.Events.Resolve<ClientGoldChangedEvent>();
        _server.Events.Resolve<ClientAttackSeenEvent>();
    }

    public ConsoleHudRenderer Renderer { get; }

    public string PluginsRoot { get; }

    public async ValueTask InstallBundlesAsync(CancellationToken ct = default)
    {
        foreach (var part in ClientBundleLoader.Load(PluginsRoot))
        {
            var required = _server.GetRequiredCapabilities(part.Package);
            Console.WriteLine($"[client] bundle {part.BundleId}/{part.Kind}: caps [{string.Join(", ", required)}]");
            if (string.Equals(part.BundleId, "gold-cheat", StringComparison.Ordinal))
            {
                Console.WriteLine("[client] DENY gold-cheat: E-POLICY-CAP (no gold bindings are registered in this host).");
                continue;
            }

            var policy = ClientPolicy.ForKernel(required);
            await InstallPartAsync(part, policy, ct).ConfigureAwait(false);
            Console.WriteLine($"[client] ALLOW {part.BundleId}: installed {part.Package.Manifest.PluginId}.");
        }
    }

    public async ValueTask<string> ClaimBountyAsync(string monsterId, CancellationToken ct = default)
    {
        if (_claimKernel is null)
        {
            throw new InvalidOperationException("Bounty claim client half is not installed.");
        }

        var result = await _claimKernel.InvokeServerExtensionAsync([SandboxValue.FromString(monsterId)], ct)
            .ConfigureAwait(false);
        return ((StringValue)result).Value;
    }

    public void Publish(ClientMonsterKilledEvent e)
        => _server.Hooks.PublishAsync(e).AsTask().GetAwaiter().GetResult();

    public void Publish(ClientGoldChangedEvent e) => _server.Subscriptions.Publish(e);

    public void Publish(ClientAttackSeenEvent e) => _server.Subscriptions.Publish(e);

    public void Dispose() => _server.Dispose();

    private async ValueTask InstallPartAsync(
        ClientBundlePart part,
        SandboxPolicy policy,
        CancellationToken ct)
    {
        switch (part.Kind)
        {
            case ClientBundlePartKind.Hook:
                _server.WireHook(await _server.InstallAsync(part.Package, policy, ct).ConfigureAwait(false));
                break;
            case ClientBundlePartKind.Subscription:
                _server.WireSubscription(await _server.InstallAsync(part.Package, policy, ct).ConfigureAwait(false));
                break;
            case ClientBundlePartKind.Extension:
                var extension = await _server.InstallServerExtensionAsync(part.Package, policy, ct).ConfigureAwait(false);
                if (string.Equals(extension.Manifest.PluginId, "bounty.claim.client", StringComparison.Ordinal))
                {
                    _claimKernel = extension;
                }

                break;
        }
    }
}
