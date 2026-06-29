using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class RpcKernelPackageValidationTests
{
    [Fact]
    public async Task Install_rejects_rpc_package_that_also_declares_event_subscriptions()
    {
        using var server = PluginServer.Create(
            configureHost: RpcKernelTestPackages.AddKillBinding,
            defaultPolicy: RpcKernelTestPackages.KillPolicy());
        var package = RpcKernelTestPackages.MonsterKiller();
        var invalid = package with
        {
            Manifest = package.Manifest with
            {
                Subscriptions = [new HookSubscriptionManifest("DamageEvent", "KillMonsters")]
            }
        };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await server.InstallServerExtensionAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK073");
    }

    [Fact]
    public async Task Install_rejects_rpc_package_with_entrypoints_that_do_not_match_rpc_entrypoint()
    {
        using var server = PluginServer.Create(
            configureHost: RpcKernelTestPackages.AddKillBinding,
            defaultPolicy: RpcKernelTestPackages.KillPolicy());
        var package = RpcKernelTestPackages.MonsterKiller();
        var invalid = package with { Entrypoints = new KernelEntrypoints("ShouldHandle", "Handle") };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await server.InstallServerExtensionAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK074");
    }

    [Fact]
    public void Prepared_rpc_validation_uses_install_policy_for_async_capability()
    {
        var package = AsyncRpcPackage();
        var plan = AsyncPlan(package);

        var ex = Assert.Throws<SandboxValidationException>(
            () => RpcKernelPackageValidator.ValidatePrepared(package, plan, NoAsyncPolicy()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK043");
    }

    private static ExecutionPlan AsyncPlan(PluginPackage package)
        => new(
            "module",
            "plan",
            new ExecutionPlanSeal("seal"),
            "policy",
            "bindings",
            package.Module,
            AsyncPolicy(),
            new BindingRegistry([]),
            new ResourceLimits(),
            new Dictionary<string, FunctionAnalysis>
            {
                ["Run"] = new(SandboxType.I32, SandboxEffect.Cpu | SandboxEffect.Concurrency, CanReorder: false)
            });

    private static PluginPackage AsyncRpcPackage()
    {
        var span = new SourceSpan(1, 1);
        var function = new SandboxFunction(
            "Run",
            IsEntrypoint: true,
            [],
            SandboxType.I32,
            [new ReturnStatement(new LiteralExpression(SandboxValue.FromInt32(1), span), span)]);
        var module = new SandboxModule(
            "async-rpc",
            SemVersion.One,
            SemVersion.One,
            [],
            [function],
            new Dictionary<string, string> { ["pluginId"] = "async-rpc", ["kernel"] = "AsyncRpcKernel" });
        var manifest = new PluginManifest(
            "async-rpc",
            "IAsyncRpcService",
            ExecutionMode.Auto,
            [nameof(SandboxEffect.Cpu), nameof(SandboxEffect.Concurrency)],
            [],
            [])
        {
            RequiredCapabilities = [RuntimeCapabilityIds.Async],
            RpcEntrypoint = "Run"
        };

        return PluginPackage.Create(manifest, module, new KernelEntrypoints("Run", "Run"));
    }

    private static SandboxPolicy AsyncPolicy()
        => SandboxPolicyBuilder.Create()
            .AllowRuntimeAsync()
            .WithFuel(10_000)
            .WithWallTime(TimeSpan.FromSeconds(5))
            .Build();

    private static SandboxPolicy NoAsyncPolicy()
        => SandboxPolicyBuilder.Create()
            .WithFuel(10_000)
            .WithWallTime(TimeSpan.FromSeconds(5))
            .Build();
}
