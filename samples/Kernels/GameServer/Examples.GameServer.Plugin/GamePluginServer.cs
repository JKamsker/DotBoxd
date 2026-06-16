namespace DotBoxD.Kernels.Game.Plugin;

/// <summary>
/// The whole facade the plugin dev writes — one line. Optionally implement <c>OnConfigured()</c> for custom
/// post-wire setup (see the commented block below), or leave it out: a bodyless partial compiles away.
/// <para>Everything else is generated from <c>: IGameWorldAccess</c>: the RPC proxy of the world, the
/// <c>IPluginServer&lt;IGameWorldAccess&gt;</c> lifecycle (<c>StartAsync</c>/<c>RunAsync</c>/
/// <c>HoldUntilShutdownAsync</c>/<c>InvokeAsync</c>), the install verbs (<c>Replace</c>/<c>Extend</c>/
/// <c>Get</c>) on the facade and its control wrappers, and <c>GamePluginServerBuilder</c>. See
/// interface-driven-plugin-server.md §7.</para>
/// </summary>
[GeneratePluginServer]
public partial class GamePluginServer : IGameWorldAccess;

// Optional custom wiring — uncomment to run after the controls connect (inside StartAsync):
//
// public partial class GamePluginServer
// {
//     partial void OnConfigured() => Console.WriteLine("[plugin] custom wiring ran.");
// }
