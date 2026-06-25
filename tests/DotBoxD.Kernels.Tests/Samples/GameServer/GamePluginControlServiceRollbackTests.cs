using DotBoxD.Plugins;
using DotBoxD.Plugins.Json;

namespace DotBoxD.Kernels.Tests.Samples.GameServer;

public sealed partial class GamePluginControlServiceRollbackTests
{
    [Fact]
    public async Task InstallPluginAsync_rejects_local_terminal_without_callback_before_install()
    {
        var (server, session, service) = CreateControlService();
        using (server)
        {
            var package = LocalTerminalPackage("local-calm");

            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await InstallPluginAsync(service, PluginPackageJsonSerializer.Export(package)));

            Assert.False(session.Owns("local-calm"));
            Assert.DoesNotContain(server.Kernels.Snapshot(), kernel => kernel.Manifest.PluginId == "local-calm");
        }
    }

    [Fact]
    public async Task Failed_local_terminal_hot_replace_keeps_existing_kernel()
    {
        var (server, session, service) = CreateControlService();
        using (server)
        {
            var incumbentPackage = ResolveGamePluginPackage("DotBoxD.Kernels.Game.Plugin.Kernels.RetaliationKernel");
            await InstallPluginAsync(service, PluginPackageJsonSerializer.Export(incumbentPackage));
            var incumbent = server.Kernels.Get("retaliation");
            var replacement = LocalTerminalPackage("retaliation");

            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await InstallPluginAsync(service, PluginPackageJsonSerializer.Export(replacement)));

            Assert.True(session.Owns("retaliation"));
            Assert.True(server.Kernels.TryGet("retaliation", out var installed));
            Assert.Same(incumbent, installed);
            Assert.False(incumbent.IsRevoked);
        }
    }

    [Fact]
    public async Task InstallPluginAsync_rolls_back_kernel_when_hook_wiring_fails_after_install()
    {
        var (server, session, service) = CreateControlService();
        using (server)
        {
            var package = ResultHookOnNonResultEventPackage("post-wire-fail");

            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await InstallPluginAsync(service, PluginPackageJsonSerializer.Export(package)));

            Assert.False(session.Owns("post-wire-fail"));
            Assert.DoesNotContain(server.Kernels.Snapshot(), kernel => kernel.Manifest.PluginId == "post-wire-fail");
        }
    }

    [Fact]
    public async Task Failed_post_install_wiring_keeps_existing_same_id_kernel()
    {
        // A same-id replacement whose WIRING fails after install must not destroy the incumbent. The new kernel
        // is installed as a non-current instance and wired before it displaces the incumbent, so a wire failure
        // rolls the staged instance back with the incumbent still current and un-revoked. (Previously the install
        // revoked the incumbent up front and rollback only removed the new install id, leaving the id with NO
        // kernel.)
        var (server, session, service) = CreateControlService();
        using (server)
        {
            var incumbentPackage = ResolveGamePluginPackage("DotBoxD.Kernels.Game.Plugin.Kernels.RetaliationKernel");
            await InstallPluginAsync(service, PluginPackageJsonSerializer.Export(incumbentPackage));
            var incumbent = server.Kernels.Get("retaliation");

            // Same plugin id, but a result hook on a non-result event: install succeeds, wiring throws.
            var brokenReplacement = ResultHookOnNonResultEventPackage("retaliation");
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await InstallPluginAsync(service, PluginPackageJsonSerializer.Export(brokenReplacement)));

            Assert.True(session.Owns("retaliation"));
            Assert.True(server.Kernels.TryGet("retaliation", out var installed));
            Assert.Same(incumbent, installed);
            Assert.False(incumbent.IsRevoked);
            // The staged (failed) instance was rolled back — only the incumbent remains under this plugin id.
            Assert.Single(server.Kernels.Snapshot(), k => k.Manifest.PluginId == "retaliation");
        }
    }

    [Fact]
    public async Task Wire_classifies_a_sandbox_result_hook_with_no_callback_as_the_Result_terminal()
    {
        // A result hook with no callback id and no ResultLocalTerminal is a sandbox Register: the verified
        // Handle returns the result in-process. The router must classify it KernelWireKind.Result (-> UseResult).
        // This is the one terminal kind no sample plugin exercises end-to-end (none use a non-local Register),
        // so it is pinned directly on the trusted classifier to complete the Plain/Projecting/Result/
        // ProjectingResult matrix (the other three are covered by RouterParityTests + the docs-smoke e2e).
        var (server, session, _) = CreateControlService();
        using (server)
        {
            var kernel = await session.InstallAsync(ResultHookOnNonResultEventPackage("sandbox-result"));

            var terminal = KernelWireTerminal.Classify(kernel, typeof(int));

            Assert.Equal(KernelWireKind.Result, terminal.Kind);
            Assert.Null(terminal.CallbackSubscriptionId);
            Assert.Equal(typeof(int), terminal.ResultType);
        }
    }
}
