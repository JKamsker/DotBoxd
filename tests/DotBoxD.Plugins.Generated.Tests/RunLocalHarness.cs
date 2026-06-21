using DotBoxD.Abstractions;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels;
using DotBoxD.Kernels.Policies;
using DotBoxD.Plugins.Policies;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Plugins.Generated.Tests;

/// <summary>
/// Shared end-to-end harness for build-time-intercepted <c>RunLocal</c> chains. It wires a real
/// <see cref="PluginServer"/>, an in-memory message sink, and the remote registry whose install callback installs
/// each generated package into the live server and forwards every server-side projection push into the local
/// handler registry the generated interceptor registers the native <c>RunLocal</c> delegate against.
/// <para>
/// A test authors a <c>harness.Hooks.On&lt;TEvent&gt;()...RunLocal(...)</c> chain — intercepted at build time by the
/// DotBoxD source generator loaded as a real analyzer (see the .csproj) — and publishes events through the server.
/// The <c>Where</c>/<c>Select</c> stages lower to verified IR that filters and projects server-side; only the
/// projected value crosses the wire, and the native terminal receives exactly that value. If interception had NOT
/// happened the real <c>RemoteHookStage.RunLocal</c> terminal would throw, so a passing assertion also proves the
/// interceptor ran.
/// </para>
/// </summary>
internal sealed class RunLocalHarness<TEvent> : IDisposable
{
    public InMemoryPluginMessageSink Sink { get; } = new();

    public PluginServer Server { get; }

    public RemoteLocalHandlerRegistry LocalHandlers { get; } = new();

    public RemoteHookRegistry Hooks { get; }

    public RunLocalHarness(SandboxPolicy? policy = null, Action<SandboxHostBuilder>? configureHost = null)
    {
        Server = PluginServer.Create(Sink, configureHost: configureHost, defaultPolicy: policy ?? TestPolicies.Chain());

        // Capture locals so the install callback does not close over `this`.
        var server = Server;
        var sink = Sink;
        var localHandlers = LocalHandlers;
        Hooks = new RemoteHookRegistry(
            async package =>
            {
                var kernel = await server.InstallAsync(package).ConfigureAwait(false);
                var subscriptionId = package.Manifest.PluginId;
                server.Hooks.On<TEvent>().UseProjecting(
                    kernel,
                    subscriptionId,
                    (id, payload, token) =>
                        localHandlers.DispatchAsync(id, payload.ToArray(), new HookContext(sink, token)));
                return subscriptionId;
            },
            localHandlers);
    }

    /// <summary>Publish an event through the live server, driving the server-side filter/projection pipeline.</summary>
    public ValueTask PublishAsync(TEvent e) => Server.Hooks.PublishAsync(e);

    public void Dispose() => Server.Dispose();
}

/// <summary>Shared sandbox policy granting everything a filter/projection chain (and a native host send) needs.</summary>
internal static class TestPolicies
{
    public static SandboxPolicy Chain()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .GrantHostMessageWrite()
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();
}
