using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionProxyNullableContractTests
{
    private const string NullableFrameworkEchoSource = """
        using System;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        public readonly record struct NullableFrameworkPair(DateOnly? Date, TimeOnly? Time);

        [ServerExtension("nullable-framework")]
        public sealed partial class NullableFrameworkKernel
        {
            public NullableFrameworkPair Echo(DateOnly? date, TimeOnly? time, HookContext ctx)
                => new(date, time);
        }
        """;

    [Fact]
    public async Task Runtime_proxy_allows_supported_nullable_framework_scalars_in_service_contracts()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            NullableFrameworkEchoSource,
            "Sample.NullableFrameworkPluginPackage");
        using var server = DotBoxD.Plugins.PluginServer.Create();
        var kernel = await server.InstallServerExtensionAsync(package);
        var service = ServerExtensionProxy.Create<INullableFrameworkEchoService>(kernel);
        DateOnly? date = new DateOnly(2026, 6, 28);
        TimeOnly? time = null;

        var result = service.Echo(date, time);

        Assert.Equal(date, result.Date);
        Assert.Null(result.Time);
    }

    [Fact]
    public async Task Runtime_proxy_rejects_unsupported_nullable_value_types_in_service_contracts()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create(
            configureHost: RpcKernelTestPackages.AddKillBinding,
            defaultPolicy: RpcKernelTestPackages.KillPolicy());
        var kernel = await server.InstallServerExtensionAsync(RpcKernelTestPackages.MonsterKiller());

        Assert.Throws<NotSupportedException>(
            () => ServerExtensionProxy.Create<INullableEchoService>(kernel));
    }

    [Fact]
    public async Task Runtime_proxy_rejects_unsupported_nullable_value_types_in_inherited_service_contracts()
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
    public async Task Runtime_proxy_rejects_generic_service_methods_even_when_the_type_parameter_is_unused()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create(
            configureHost: RpcKernelTestPackages.AddKillBinding,
            defaultPolicy: RpcKernelTestPackages.KillPolicy());
        var kernel = await server.InstallServerExtensionAsync(RpcKernelTestPackages.MonsterKiller());

        var exception = Assert.Throws<NotSupportedException>(
            () => ServerExtensionProxy.Create<IUnusedGenericMonsterKillerService>(kernel));
        Assert.Contains("non-generic", exception.Message, StringComparison.Ordinal);
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
        int Echo(DateTime? value);
    }

    private interface INullableEchoBaseService
    {
        int Echo(DateTime? value);
    }

    private interface IInheritedNullableEchoService : INullableEchoBaseService;

    private interface INullableFrameworkEchoService
    {
        NullableFrameworkPair Echo(DateOnly? date, TimeOnly? time);
    }

    private readonly record struct NullableFrameworkPair(DateOnly? Date, TimeOnly? Time);

    private interface IPropertyOnlyService
    {
        int Value { get; }
    }

    private interface INestedTaskLikeReturnService
    {
        ValueTask<ValueTask<int>> EchoAsync(int value);
    }

    private interface IUnusedGenericMonsterKillerService
    {
        List<KillResult> KillMonsters<T>(List<int> monsterIds);
    }

    private interface INullDefaultEchoService
    {
        int Echo(string value = null!);
    }
}
