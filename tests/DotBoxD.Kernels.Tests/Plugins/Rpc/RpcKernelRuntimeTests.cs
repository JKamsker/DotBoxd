using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Json;
using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

/// <summary>
/// Runtime proof of the kernel RPC service path (Followup #2): a hand-built batch kernel loops over a
/// <c>List&lt;I32&gt;</c> input server-side, calls a host binding per element, accumulates a
/// <c>List&lt;Record&gt;</c> (a list of objects), and returns it in one <see cref="InstalledKernel.InvokeRpcAsync"/>
/// roundtrip — the result is returned, not discarded. Also proves the package (including the manifest's
/// rpcEntrypoint) survives a JSON export/import round-trip and that capability gating still applies.
/// </summary>
public sealed class RpcKernelRuntimeTests
{
    private const string ConcurrencyEffectSource = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        public interface IConcurrentWorld
        {
            [HostBinding("host.concurrent.value", "host.concurrent.value", SandboxEffect.Cpu | SandboxEffect.Concurrency)]
            int Value();
        }

        [KernelRpcService("concurrency-effect")]
        public sealed partial class ConcurrencyEffectKernel
        {
            public int Run(HookContext ctx)
            {
                return ctx.Host<IConcurrentWorld>().Value();
            }
        }
        """;

    [Fact]
    public async Task A_batch_kernel_loops_server_side_and_returns_a_list_of_records()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create(configureHost: RpcKernelTestPackages.AddKillBinding, defaultPolicy: RpcKernelTestPackages.KillPolicy());
        var kernel = await server.InstallRpcAsync(RpcKernelTestPackages.MonsterKiller());

        var ids = SandboxValue.FromList(
            [SandboxValue.FromInt32(1), SandboxValue.FromInt32(2), SandboxValue.FromInt32(3), SandboxValue.FromInt32(4)],
            SandboxType.I32);

        var result = await kernel.InvokeRpcAsync([ids]);

        var list = Assert.IsType<ListValue>(result);
        Assert.Equal(4, list.Values.Count);   // one record per monster id, built in one roundtrip
        // Kill succeeds for even ids; each result record is { MonsterId, Success }.
        AssertKill(list.Values[0], 1, false);
        AssertKill(list.Values[1], 2, true);
        AssertKill(list.Values[2], 3, false);
        AssertKill(list.Values[3], 4, true);
    }

    [Fact]
    public async Task A_batch_kernel_compiles_to_valid_il_and_returns_the_same_result()
    {
        // Compiled execution runs the IL compiler + verifier over the batch IR (forRange, list.empty
        // <Record>, list.add, record.new, the host binding). A passing run proves the emitted IL is valid
        // and fast — the result matches the interpreted batch kernel.
        using var server = DotBoxD.Plugins.PluginServer.Create(
            configureHost: RpcKernelTestPackages.AddKillBinding,
            defaultPolicy: RpcKernelTestPackages.KillPolicy(),
            executionMode: ExecutionMode.Compiled);
        var kernel = await server.InstallRpcAsync(RpcKernelTestPackages.MonsterKiller());

        var ids = SandboxValue.FromList(
            [SandboxValue.FromInt32(1), SandboxValue.FromInt32(2), SandboxValue.FromInt32(3)],
            SandboxType.I32);
        var result = await kernel.InvokeRpcAsync([ids]);

        var list = Assert.IsType<ListValue>(result);
        Assert.Equal(3, list.Values.Count);
        AssertKill(list.Values[0], 1, false);
        AssertKill(list.Values[1], 2, true);
        AssertKill(list.Values[2], 3, false);
    }

    [Fact]
    public async Task A_batch_kernel_round_trips_through_json_and_runs()
    {
        var json = PluginPackageJsonSerializer.Export(RpcKernelTestPackages.MonsterKiller(), indented: true);
        var imported = PluginPackageJsonSerializer.Import(json);
        Assert.Equal("KillMonsters", imported.Manifest.RpcEntrypoint);

        using var server = DotBoxD.Plugins.PluginServer.Create(configureHost: RpcKernelTestPackages.AddKillBinding, defaultPolicy: RpcKernelTestPackages.KillPolicy());
        var kernel = await server.InstallRpcAsync(imported);

        var result = await kernel.InvokeRpcAsync([SandboxValue.FromList([SandboxValue.FromInt32(2)], SandboxType.I32)]);

        var list = Assert.IsType<ListValue>(result);
        AssertKill(Assert.Single(list.Values), 2, true);
    }

    [Fact]
    public async Task InstallJsonAsync_routes_rpc_packages_to_rpc_installer()
    {
        var json = PluginPackageJsonSerializer.Export(RpcKernelTestPackages.MonsterKiller(), indented: true);
        using var server = DotBoxD.Plugins.PluginServer.Create(
            configureHost: RpcKernelTestPackages.AddKillBinding,
            defaultPolicy: RpcKernelTestPackages.KillPolicy());

        var kernel = await server.InstallJsonAsync(json);

        var result = await kernel.InvokeRpcAsync(
            [SandboxValue.FromList([SandboxValue.FromInt32(2)], SandboxType.I32)]);
        var list = Assert.IsType<ListValue>(result);
        AssertKill(Assert.Single(list.Values), 2, true);
    }

    [Fact]
    public async Task SessionInstallJsonAsync_routes_rpc_packages_to_rpc_installer()
    {
        var json = PluginPackageJsonSerializer.Export(RpcKernelTestPackages.MonsterKiller(), indented: true);
        using var server = DotBoxD.Plugins.PluginServer.Create(
            configureHost: RpcKernelTestPackages.AddKillBinding,
            defaultPolicy: RpcKernelTestPackages.KillPolicy());
        using var session = server.CreateSession();

        var kernel = await session.InstallJsonAsync(json);

        Assert.True(session.Owns("monster-killer"));
        var result = await kernel.InvokeRpcAsync(
            [SandboxValue.FromList([SandboxValue.FromInt32(2)], SandboxType.I32)]);
        var list = Assert.IsType<ListValue>(result);
        AssertKill(Assert.Single(list.Values), 2, true);
    }

    [Fact]
    public async Task Install_rejects_rpc_package_without_module_kernel_metadata()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create(
            configureHost: RpcKernelTestPackages.AddKillBinding,
            defaultPolicy: RpcKernelTestPackages.KillPolicy());
        var package = RpcKernelTestPackages.MonsterKiller();
        var metadata = package.Module.Metadata
            .Where(pair => pair.Key != PluginManifestNames.ModuleMetadata.Kernel)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var invalid = package with { Module = package.Module with { Metadata = metadata } };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await server.InstallRpcAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK013");
    }

    [Fact]
    public async Task A_batch_kernel_is_denied_when_its_capability_is_not_granted()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create(configureHost: RpcKernelTestPackages.AddKillBinding, defaultPolicy: RpcKernelTestPackages.NoKillPolicy());

        await Assert.ThrowsAnyAsync<Exception>(async () => await server.InstallRpcAsync(RpcKernelTestPackages.MonsterKiller()).AsTask());
    }

    [Fact]
    public async Task Install_rejects_rpc_manifest_required_capabilities_that_do_not_match_verified_module()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create(
            configureHost: RpcKernelTestPackages.AddKillBinding,
            defaultPolicy: RpcKernelTestPackages.KillPolicy());
        var package = RpcKernelTestPackages.MonsterKiller();
        var invalid = package with { Manifest = package.Manifest with { RequiredCapabilities = [] } };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await server.InstallRpcAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK044");
    }

    [Fact]
    public async Task Install_accepts_manifest_async_capability_for_concurrency_effect_binding()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            ConcurrencyEffectSource,
            "Sample.ConcurrencyEffectPluginPackage");
        Assert.Contains(RuntimeCapabilityIds.Async, package.Manifest.RequiredCapabilities);

        using var server = DotBoxD.Plugins.PluginServer.Create(
            configureHost: AddConcurrencyEffectBinding,
            defaultPolicy: ConcurrencyEffectPolicy());

        var kernel = await server.InstallRpcAsync(package);
        var result = await kernel.InvokeRpcAsync([]);

        Assert.Equal(7, Assert.IsType<I32Value>(result).Value);
    }

    [Fact]
    public async Task Install_rejects_rpc_manifest_required_capabilities_that_self_assert_unverified_capability()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create(
            configureHost: RpcKernelTestPackages.AddKillBinding,
            defaultPolicy: RpcKernelTestPackages.KillPolicy());
        var package = RpcKernelTestPackages.MonsterKiller();
        var invalid = package with
        {
            Manifest = package.Manifest with
            {
                RequiredCapabilities = [.. package.Manifest.RequiredCapabilities, "file.write"]
            }
        };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await server.InstallRpcAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK044");
    }

    [Fact]
    public async Task Invoking_with_the_wrong_argument_count_throws()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create(configureHost: RpcKernelTestPackages.AddKillBinding, defaultPolicy: RpcKernelTestPackages.KillPolicy());
        var kernel = await server.InstallRpcAsync(RpcKernelTestPackages.MonsterKiller());

        await Assert.ThrowsAsync<SandboxRuntimeException>(async () => await kernel.InvokeRpcAsync([]).AsTask());
    }

    private static void AssertKill(SandboxValue value, int expectedId, bool expectedSuccess)
    {
        var record = Assert.IsType<RecordValue>(value);
        Assert.Equal([SandboxValue.FromInt32(expectedId), SandboxValue.FromBool(expectedSuccess)], record.Fields);
    }

    private static void AddConcurrencyEffectBinding(SandboxHostBuilder builder)
        => builder.AddBinding(new BindingDescriptor(
            "host.concurrent.value",
            SemVersion.One,
            [],
            SandboxType.I32,
            SandboxEffect.Cpu | SandboxEffect.Concurrency,
            "host.concurrent.value",
            BindingCostModel.Fixed(1),
            AuditLevel.PerResource,
            BindingSafety.PureHostFacade,
            (context, _, _) =>
            {
                var startedAt = DateTimeOffset.UtcNow;
                context.Audit.Write(new SandboxAuditEvent(
                    context.RunId,
                    "BindingCall",
                    startedAt,
                    true,
                    BindingId: "host.concurrent.value",
                    CapabilityId: "host.concurrent.value",
                    Effect: SandboxEffect.Concurrency,
                    ResourceId: "host.concurrent.value",
                    Fields: context.BindingAuditFields("host", startedAt)));
                return ValueTask.FromResult(SandboxValue.FromInt32(7));
            },
            CompiledBinding.RuntimeStub("DotBoxD.Kernels.Runtime.CompiledRuntime", "CallBinding"),
            GrantValidator: static (_, _) => { }));

    private static SandboxPolicy ConcurrencyEffectPolicy()
        => SandboxPolicyBuilder.Create()
            .Grant("host.concurrent.value", new { }, SandboxEffect.Concurrency)
            .AllowRuntimeAsync()
            .WithFuel(100_000)
            .WithMaxHostCalls(10_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();
}
