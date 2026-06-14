using System.Reflection;
using DotBoxd.Hosting;
using DotBoxd.Kernels.PluginLocal;
using DotBoxd.Plugins;

namespace DotBoxd.Kernels.Tests;

/// <summary>
/// Regression coverage for API-0017: <see cref="DotBoxd.Plugins.PluginServer"/> constructs and owns an
/// internal <see cref="DotBoxd.Hosting.SandboxHost"/> (via <c>PluginServer.Create</c>), yet exposes no
/// disposal surface. The lower-level host makes ownership explicit with <see cref="System.IDisposable"/>
/// (it disposes the compiled executable cache and other host-owned execution state), but the
/// higher-level plugin convenience type hides that owned host and gives callers no equivalent release
/// path. A host that creates and retires plugin servers (per tenant, world, test, or reload) therefore
/// cannot deterministically release compiled materializations and other host-owned resources through
/// the public plugin API.
///
/// These tests pin the expected public lifetime surface using reflection so the file compiles against
/// the current public API: they reference no member the fix will add by name and never cast to / pattern
/// match against a not-yet-implemented interface (which would warn on the sealed type under
/// TreatWarningsAsErrors). They are RED today because <see cref="PluginServer"/> does not implement
/// <see cref="System.IDisposable"/>, and turn GREEN once a disposal surface that forwards to the owned
/// host is added.
/// </summary>
public sealed class Fix_API_0017_Tests
{
    [Fact]
    public void PluginServer_exposes_a_disposal_surface_for_its_owned_host()
    {
        // The owned host already makes ownership explicit; the convenience wrapper must too.
        Assert.True(
            typeof(IDisposable).IsAssignableFrom(typeof(SandboxHost)),
            "Control: the owned SandboxHost is expected to be IDisposable.");

        Assert.True(
            typeof(IDisposable).IsAssignableFrom(typeof(PluginServer)),
            "PluginServer owns a SandboxHost but exposes no disposal surface (IDisposable). " +
            "Callers retiring a plugin server cannot deterministically release host-owned " +
            "execution resources through the public plugin API.");
    }

    [Fact]
    public async Task A_created_plugin_server_with_an_installed_package_is_disposable()
    {
        // Build a real plugin server over the default compiled-capable host configuration and install
        // a package, mirroring a host that wires plugins through the public convenience surface. This
        // confirms the disposal-surface assertion below is the only thing red, not test setup.
        var server = PluginServer.Create(
            defaultPolicy: PluginAddendumTestPolicies.LongWall());
        await server.InstallAsync(FireDamagePluginPackage.Create());

        // The correct, post-fix behavior is that a created plugin server is itself disposable and
        // forwards disposal to the owned host. We probe the disposal contract reflectively (rather than
        // naming a member that does not yet exist), so this compiles against the current API and is red
        // purely because the contract is missing today.
        Assert.True(
            typeof(IDisposable).IsAssignableFrom(server.GetType()),
            "PluginServer must expose IDisposable so retiring a plugin server releases the owned " +
            "SandboxHost (compiled executable cache, generated load contexts, hotness state).");

        InvokeDisposeIfPresent(server);
    }

    /// <summary>
    /// Invokes <c>Dispose()</c> through the runtime type if (and only if) the disposal surface exists.
    /// Until the fix lands this is unreachable because the assertion above already failed; afterward it
    /// proves a created server can be deterministically released through the public API.
    /// </summary>
    private static void InvokeDisposeIfPresent(PluginServer server)
    {
        var dispose = server.GetType().GetMethod(
            nameof(IDisposable.Dispose),
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);

        dispose?.Invoke(server, parameters: null);
    }
}
