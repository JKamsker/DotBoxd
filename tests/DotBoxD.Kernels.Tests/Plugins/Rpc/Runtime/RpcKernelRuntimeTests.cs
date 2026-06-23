using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Json;
using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

/// <summary>
/// Runtime proof of the server extension path (Followup #2): a hand-built batch kernel loops over a
/// <c>List&lt;I32&gt;</c> input server-side, calls a host binding per element, accumulates a
/// <c>List&lt;Record&gt;</c> (a list of objects), and returns it in one <see cref="InstalledKernel.InvokeServerExtensionAsync"/>
/// roundtrip — the result is returned, not discarded. Also proves the package (including the manifest's
/// rpcEntrypoint) survives a JSON export/import round-trip and that capability gating still applies.
/// </summary>
public sealed class RpcKernelRuntimeTests
{
    private static readonly SourceSpan Span = new(1, 1);

    [Fact]
    public async Task A_batch_kernel_loops_server_side_and_returns_a_list_of_records()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create(configureHost: RpcKernelTestPackages.AddKillBinding, defaultPolicy: RpcKernelTestPackages.KillPolicy());
        var kernel = await server.InstallServerExtensionAsync(RpcKernelTestPackages.MonsterKiller());

        var ids = SandboxValue.FromList(
            [SandboxValue.FromInt32(1), SandboxValue.FromInt32(2), SandboxValue.FromInt32(3), SandboxValue.FromInt32(4)],
            SandboxType.I32);

        var result = await kernel.InvokeServerExtensionAsync([ids]);

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
        var kernel = await server.InstallServerExtensionAsync(RpcKernelTestPackages.MonsterKiller());

        var ids = SandboxValue.FromList(
            [SandboxValue.FromInt32(1), SandboxValue.FromInt32(2), SandboxValue.FromInt32(3)],
            SandboxType.I32);
        var result = await kernel.InvokeServerExtensionAsync([ids]);

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
        var kernel = await server.InstallServerExtensionAsync(imported);

        var result = await kernel.InvokeServerExtensionAsync([SandboxValue.FromList([SandboxValue.FromInt32(2)], SandboxType.I32)]);

        var list = Assert.IsType<ListValue>(result);
        AssertKill(Assert.Single(list.Values), 2, true);
    }

    [Fact]
    public async Task A_batch_kernel_with_many_helper_functions_uses_the_manifest_rpc_entrypoint()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create(
            configureHost: RpcKernelTestPackages.AddKillBinding,
            defaultPolicy: RpcKernelTestPackages.KillPolicy());
        var kernel = await server.InstallServerExtensionAsync(WithHelperFunctions(RpcKernelTestPackages.MonsterKiller(), 128));

        var ids = SandboxValue.FromList([SandboxValue.FromInt32(6)], SandboxType.I32);

        var result = await kernel.InvokeServerExtensionAsync([ids]);

        var list = Assert.IsType<ListValue>(result);
        AssertKill(Assert.Single(list.Values), 6, true);
    }

    [Fact]
    public void Required_capabilities_are_derived_from_registered_host_bindings()
    {
        var package = RpcKernelTestPackages.MonsterKiller();
        package = package with { Manifest = package.Manifest with { RequiredCapabilities = [] } };
        using var server = DotBoxD.Plugins.PluginServer.Create(configureHost: RpcKernelTestPackages.AddKillBinding);

        var capabilities = server.GetRequiredCapabilities(package);

        Assert.Contains(RpcKernelTestPackages.KillCapability, capabilities);
        Assert.Equal(capabilities.Order(StringComparer.Ordinal), capabilities);
    }

    [Fact]
    public async Task A_batch_kernel_is_denied_when_its_capability_is_not_granted()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create(configureHost: RpcKernelTestPackages.AddKillBinding, defaultPolicy: RpcKernelTestPackages.NoKillPolicy());

        await Assert.ThrowsAnyAsync<Exception>(async () => await server.InstallServerExtensionAsync(RpcKernelTestPackages.MonsterKiller()).AsTask());
    }

    [Fact]
    public async Task Invoking_with_the_wrong_argument_count_throws()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create(configureHost: RpcKernelTestPackages.AddKillBinding, defaultPolicy: RpcKernelTestPackages.KillPolicy());
        var kernel = await server.InstallServerExtensionAsync(RpcKernelTestPackages.MonsterKiller());

        await Assert.ThrowsAsync<SandboxRuntimeException>(async () => await kernel.InvokeServerExtensionAsync([]).AsTask());
    }

    private static void AssertKill(SandboxValue value, int expectedId, bool expectedSuccess)
    {
        var record = Assert.IsType<RecordValue>(value);
        Assert.Equal([SandboxValue.FromInt32(expectedId), SandboxValue.FromBool(expectedSuccess)], record.Fields);
    }

    private static PluginPackage WithHelperFunctions(PluginPackage package, int helperCount)
    {
        var functions = new SandboxFunction[helperCount + package.Module.Functions.Count];
        for (var i = 0; i < helperCount; i++)
        {
            functions[i] = HelperFunction(i);
        }

        for (var i = 0; i < package.Module.Functions.Count; i++)
        {
            functions[helperCount + i] = package.Module.Functions[i];
        }

        return package with { Module = package.Module with { Functions = functions } };
    }

    private static SandboxFunction HelperFunction(int index)
        => new(
            "Helper" + index.ToString("D3"),
            IsEntrypoint: false,
            [],
            SandboxType.I32,
            [new ReturnStatement(new LiteralExpression(SandboxValue.FromInt32(index), Span), Span)]);
}
