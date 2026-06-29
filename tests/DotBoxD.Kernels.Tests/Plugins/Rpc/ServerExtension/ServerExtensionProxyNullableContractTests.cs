using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionProxyNullableContractTests
{
    [Fact]
    public async Task Runtime_proxy_rejects_nullable_value_types_in_service_contracts()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create(
            configureHost: RpcKernelTestPackages.AddKillBinding,
            defaultPolicy: RpcKernelTestPackages.KillPolicy());
        var kernel = await server.InstallServerExtensionAsync(RpcKernelTestPackages.MonsterKiller());

        Assert.Throws<NotSupportedException>(
            () => ServerExtensionProxy.Create<INullableEchoService>(kernel));
    }

    [Fact]
    public async Task Runtime_proxy_rejects_nullable_value_types_in_inherited_service_contracts()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create(
            configureHost: RpcKernelTestPackages.AddKillBinding,
            defaultPolicy: RpcKernelTestPackages.KillPolicy());
        var kernel = await server.InstallServerExtensionAsync(RpcKernelTestPackages.MonsterKiller());

        Assert.Throws<NotSupportedException>(
            () => ServerExtensionProxy.Create<IInheritedNullableEchoService>(kernel));
    }

    [Fact]
    public async Task Runtime_proxy_rejects_property_accessors_in_service_contracts()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create(
            configureHost: RpcKernelTestPackages.AddKillBinding,
            defaultPolicy: RpcKernelTestPackages.KillPolicy());
        var kernel = await server.InstallServerExtensionAsync(RpcKernelTestPackages.MonsterKiller());

        var exception = Assert.Throws<NotSupportedException>(
            () => ServerExtensionProxy.Create<IPropertyOnlyService>(kernel));
        Assert.Contains("method", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Runtime_proxy_rejects_nested_task_like_return_payloads()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create(
            configureHost: RpcKernelTestPackages.AddKillBinding,
            defaultPolicy: RpcKernelTestPackages.KillPolicy());
        var kernel = await server.InstallServerExtensionAsync(RpcKernelTestPackages.MonsterKiller());

        var exception = Assert.Throws<NotSupportedException>(
            () => ServerExtensionProxy.Create<INestedTaskLikeReturnService>(kernel));
        Assert.Contains("task-like", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Runtime_proxy_rejects_service_null_reference_defaults()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create(
            configureHost: RpcKernelTestPackages.AddKillBinding,
            defaultPolicy: RpcKernelTestPackages.KillPolicy());
        var kernel = await server.InstallServerExtensionAsync(RpcKernelTestPackages.MonsterKiller());

        var exception = Assert.Throws<NotSupportedException>(
            () => ServerExtensionProxy.Create<INullDefaultEchoService>(kernel));
        Assert.Contains("default to null", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private interface INullableEchoService
    {
        int Echo(int? value);
    }

    private interface INullableEchoBaseService
    {
        int Echo(int? value);
    }

    private interface IInheritedNullableEchoService : INullableEchoBaseService;

    private interface IPropertyOnlyService
    {
        int Value { get; }
    }

    private interface INestedTaskLikeReturnService
    {
        ValueTask<ValueTask<int>> EchoAsync(int value);
    }

    private interface INullDefaultEchoService
    {
        int Echo(string value = null!);
    }
}
