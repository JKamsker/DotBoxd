using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Json;
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
    public async Task Install_rejects_rpc_package_that_self_asserts_event_property_capability()
    {
        using var server = PluginServer.Create(
            configureHost: RpcKernelTestPackages.AddKillBinding,
            defaultPolicy: RpcKernelTestPackages.KillPolicy());
        var package = RpcKernelTestPackages.MonsterKiller();
        var invalid = WithEventReadCapability(package);

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await server.InstallServerExtensionAsync(invalid, KillAndEventReadPolicy()).AsTask());

        Assert.Contains(ex.Diagnostics, d =>
            d.Code == "DBXK044" &&
            d.Message.Contains("event.read.secret", StringComparison.Ordinal));
    }

    [Fact]
    public void Server_required_capability_analysis_excludes_rpc_event_property_manifest_capabilities()
    {
        using var server = PluginServer.Create(
            configureHost: RpcKernelTestPackages.AddKillBinding,
            defaultPolicy: RpcKernelTestPackages.KillPolicy());
        var package = WithEventReadCapability(RpcKernelTestPackages.MonsterKiller());

        var required = server.GetRequiredCapabilities(package);

        Assert.Contains(RpcKernelTestPackages.KillCapability, required);
        Assert.DoesNotContain("event.read.secret", required);
    }

    [Fact]
    public async Task Install_rejects_rpc_package_with_invalid_manifest_plugin_id_shape()
    {
        using var server = PluginServer.Create(
            configureHost: RpcKernelTestPackages.AddKillBinding,
            defaultPolicy: RpcKernelTestPackages.KillPolicy());
        var invalid = WithPluginId(RpcKernelTestPackages.MonsterKiller(), "../monster/killer");

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await server.InstallServerExtensionAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d =>
            d.Code == "DBXK050" &&
            d.Message.Contains("plugin id", StringComparison.OrdinalIgnoreCase) &&
            d.Message.Contains("identifier", StringComparison.OrdinalIgnoreCase));
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
    public void Import_rejects_rpc_package_with_missing_handle_entrypoint_alias()
    {
        var json = PluginPackageJsonSerializer.Export(RpcKernelTestPackages.MonsterKiller())
            .Replace("\"KillMonsters\"", "\"Handle\"", StringComparison.Ordinal)
            .Replace(
                "\"entrypoints\":{\"shouldHandle\":\"Handle\",\"handle\":\"Handle\"}",
                "\"entrypoints\":{\"shouldHandle\":\"Handle\"}",
                StringComparison.Ordinal);

        var ex = Assert.Throws<SandboxValidationException>(
            () => PluginPackageJsonSerializer.Import(json));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-JSON-MISSING");
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

    private static PluginPackage WithEventReadCapability(PluginPackage package)
        => package with
        {
            Manifest = package.Manifest with
            {
                RequiredCapabilities = [.. package.Manifest.RequiredCapabilities, "event.read.secret"]
            }
        };

    private static PluginPackage WithPluginId(PluginPackage package, string pluginId)
    {
        var metadata = new Dictionary<string, string>(package.Module.Metadata, StringComparer.Ordinal)
        {
            ["pluginId"] = pluginId
        };

        return package with
        {
            Manifest = package.Manifest with { PluginId = pluginId },
            Module = package.Module with { Id = pluginId, Metadata = metadata }
        };
    }

    private static SandboxPolicy KillAndEventReadPolicy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .Grant("game.world.monster.write.*", new { }, SandboxEffect.HostStateWrite)
            .Grant("event.read.*", new { }, SandboxEffect.None)
            .WithFuel(100_000)
            .WithMaxHostCalls(10_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();
}
