using DotBoxD.Abstractions;
using DotBoxD.Kernels;
using DotBoxD.Kernels.Policies;
using DotBoxD.Plugins.Policies;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Plugins.Generated.Tests;

/// <summary>A flat scalar event, the shape a plugin author would subscribe to.</summary>
public sealed record MonsterAggroEvent(string MonsterId, string Zone, int Distance);

/// <summary>
/// Behavioural coverage for a terminal anonymous-type <c>RunLocal</c> projection authored as ORDINARY code, with
/// the DotBoxD source generator loaded as a real build-time analyzer (see the .csproj). The generator intercepts
/// the <c>RunLocal</c> call site in this very project — no dynamic compilation and no reflection over a generated
/// assembly — so the test exercises exactly the code a plugin/SDK consumer ships.
/// </summary>
public sealed class AnonymousTerminalProjectionTests
{
    [Fact]
    public async Task A_terminal_anonymous_projection_is_intercepted_and_runs_end_to_end()
    {
        var received = new List<string>();
        var sink = new InMemoryPluginMessageSink();
        using var server = PluginServer.Create(sink, defaultPolicy: ChainPolicy());
        var localHandlers = new RemoteLocalHandlerRegistry();

        // The plugin-side authoring registry: its install callback installs the generated package into the live
        // server and forwards each server push into the local handler registry the interceptor registers against.
        var hooks = new RemoteHookRegistry(
            async package =>
            {
                var kernel = await server.InstallAsync(package).ConfigureAwait(false);
                var subscriptionId = package.Manifest.PluginId;
                server.Hooks.On<MonsterAggroEvent>().UseProjecting(
                    kernel,
                    subscriptionId,
                    (id, payload, token) => localHandlers.DispatchAsync(id, payload.ToArray(), new HookContext(sink, token)));
                return subscriptionId;
            },
            localHandlers);

        // === Real usage ===
        // The build-time source generator intercepts THIS RunLocal call site. Where/Select lower to verified IR
        // that filters and projects the anonymous { Id, Zone } server-side; the projected value crosses the wire to
        // the native RunLocal delegate. The projected type is anonymous — never named in any generated source — so
        // the interceptor and decoder are emitted generic and the decoder constructs the same anonymous shape with a
        // source-generated object literal. (If the interceptor had NOT been generated, the real RemoteHookStage.RunLocal
        // would throw, so this also proves interception ran.)
        hooks.On<MonsterAggroEvent>()
            .Where(e => e.Distance <= 4)
            .Select(e => new { Id = e.MonsterId, e.Zone })
            .RunLocal((x, ctx) => received.Add($"{x.Id}|{x.Zone}"));

        await server.Hooks.PublishAsync(new MonsterAggroEvent("m-1", "crypt", 3));   // 3 <= 4 -> projected + pushed
        await server.Hooks.PublishAsync(new MonsterAggroEvent("m-2", "void", 99));   // 99 > 4 -> filtered server-side

        Assert.Equal("m-1|crypt", Assert.Single(received));
    }

    private static SandboxPolicy ChainPolicy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .GrantHostMessageWrite()
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .WithWallTime(System.TimeSpan.FromSeconds(10))
            .Build();
}
