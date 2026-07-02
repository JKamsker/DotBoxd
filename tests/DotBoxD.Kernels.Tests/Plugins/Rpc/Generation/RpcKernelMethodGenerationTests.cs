using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class RpcKernelMethodGenerationTests
{
    [Fact]
    public async Task ServerExtension_inlines_static_KernelMethod_helper()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            """
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            [ServerExtension("kernel-method-helper")]
            public sealed partial class HelperKernel
            {
                public int Run(int value, HookContext ctx)
                {
                    return AddOne(value);
                }

                [KernelMethod]
                public static int AddOne(int value) => value + 1;
            }
            """,
            "Sample.HelperPluginPackage");

        using var server = PluginServer.Create(defaultPolicy: PurePolicy());
        var kernel = await server.InstallServerExtensionAsync(package);
        var result = await kernel.InvokeServerExtensionAsync([SandboxValue.FromInt32(41)]);

        Assert.Equal(42, Assert.IsType<I32Value>(result).Value);
    }

    [Fact]
    public async Task ServerExtension_inlines_static_extension_KernelMethod_helper_with_named_default_arguments()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            """
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            [ServerExtension("kernel-method-extension-helper")]
            public sealed partial class HelperKernel
            {
                public int Run(int value, HookContext ctx)
                {
                    return value.Add(delta: 1);
                }
            }

            public static class HelperMethods
            {
                [KernelMethod]
                public static int Add(this int value, int delta = 1) => value + delta;
            }
            """,
            "Sample.HelperPluginPackage");

        using var server = PluginServer.Create(defaultPolicy: PurePolicy());
        var kernel = await server.InstallServerExtensionAsync(package);
        var result = await kernel.InvokeServerExtensionAsync([SandboxValue.FromInt32(41)]);

        Assert.Equal(42, Assert.IsType<I32Value>(result).Value);
    }

    [Fact]
    public async Task ServerExtension_inlines_generated_context_instance_KernelMethod_helper()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;
            using DotBoxD.Services.Attributes;

            namespace Sample
            {
                [RpcService]
                public interface IGameWorld;

                [GeneratePluginServer(Context = typeof(GamePluginContext))]
                public partial class RemotePluginServer : IGameWorld;

                public sealed partial class GamePluginContext
                {
                    [KernelMethod]
                    public int AddOne(int value) => value + 1;
                }
            }

            namespace Sample.Ipc
            {
                public readonly record struct LiveSettingUpdate(string Name, string Value);

                public interface IGamePluginControlService : DotBoxD.Plugins.IServerExtensionWireClient
                {
                    ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default);
                    ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default);
                    ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default);
                    ValueTask UpdateSettingsAsync(
                        string pluginId,
                        LiveSettingUpdate[] updates,
                        bool atomic = false,
                        CancellationToken ct = default);
                    ValueTask HoldUntilShutdownAsync(CancellationToken ct = default);
                }
            }

            namespace DotBoxD.Services.Generated
            {
                public static class DotBoxDGeneratedExtensions
                {
                    public static Sample.IGameWorld GetGameWorld(DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new System.InvalidOperationException("not used");
                }
            }

            namespace Sample
            {
                [ServerExtension("context-kernel-helper")]
                public sealed partial class ContextHelperKernel
                {
                    public int Run(int value, GamePluginContext ctx)
                    {
                        return ctx.AddOne(value);
                    }
                }
            }
            """,
            "Sample.ContextHelperPluginPackage");

        using var server = PluginServer.Create(defaultPolicy: PurePolicy());
        var kernel = await server.InstallServerExtensionAsync(package);
        var result = await kernel.InvokeServerExtensionAsync([SandboxValue.FromInt32(41)]);

        Assert.Equal(42, Assert.IsType<I32Value>(result).Value);
    }

    [Fact]
    public async Task ServerExtension_evaluates_KernelMethod_arguments_once()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            """
            using DotBoxD.Kernels; using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins; using DotBoxD.Abstractions;
            namespace Sample;

            public interface IProbeWorld
            {
                [HostBinding("host.probe.next", "probe.read.next", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                int Next(int value);
            }
            [ServerExtension("kernel-method-evaluate-once")]
            public sealed partial class HelperKernel
            {
                public int Run(int value, HookContext ctx)
                {
                    return AddTwice(ctx.Host<IProbeWorld>().Next(value));
                }
                [KernelMethod]
                public static int AddTwice(int value) => value + value;
            }
            """,
            "Sample.HelperPluginPackage");
        var calls = 0;
        using var server = PluginServer.Create(
            configureHost: builder => AddNextBinding(builder, () => ++calls),
            defaultPolicy: ProbeReadPolicy());

        var kernel = await server.InstallServerExtensionAsync(package);
        var result = await kernel.InvokeServerExtensionAsync([SandboxValue.FromInt32(20)]);

        Assert.Equal(42, Assert.IsType<I32Value>(result).Value);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ServerExtension_evaluates_KernelMethod_named_arguments_in_call_order()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            """
            using DotBoxD.Kernels; using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins; using DotBoxD.Abstractions;
            namespace Sample;

            public interface IProbeWorld
            {
                [HostBinding("host.probe.next", "probe.read.next", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                int Next(int value);
            }
            [ServerExtension("kernel-method-named-argument-order")]
            public sealed partial class HelperKernel
            {
                public int Run(HookContext ctx)
                {
                    return Subtract(right: ctx.Host<IProbeWorld>().Next(100), left: ctx.Host<IProbeWorld>().Next(10));
                }
                [KernelMethod]
                public static int Subtract(int left, int right) => left - right;
            }
            """,
            "Sample.HelperPluginPackage");
        var calls = 0;
        using var server = PluginServer.Create(
            configureHost: builder => AddNextBinding(builder, () => ++calls),
            defaultPolicy: ProbeReadPolicy());

        var kernel = await server.InstallServerExtensionAsync(package);
        var result = await kernel.InvokeServerExtensionAsync([]);

        Assert.Equal(-89, Assert.IsType<I32Value>(result).Value);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ServerExtension_KernelMethod_string_default_declares_allocation_effect()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            """
            using DotBoxD.Plugins; using DotBoxD.Abstractions;
            namespace Sample;

            [ServerExtension("kernel-method-string-default")]
            public sealed partial class HelperKernel
            {
                public string Run(HookContext ctx)
                {
                    return Echo();
                }

                [KernelMethod]
                public static string Echo(string value = "fallback") => value;
            }
            """,
            "Sample.HelperPluginPackage");

        Assert.Contains("Alloc", package.Manifest.Effects);

        using var server = PluginServer.Create(defaultPolicy: PurePolicy());
        var kernel = await server.InstallServerExtensionAsync(package);
        var result = await kernel.InvokeServerExtensionAsync([]);

        Assert.Equal("fallback", Assert.IsType<StringValue>(result).Value);
    }

    [Fact]
    public void ServerExtension_rejects_unsupported_KernelMethod_default_parameter_type()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(
            """
            using DotBoxD.Plugins; using DotBoxD.Abstractions;
            namespace Sample;

            [ServerExtension("kernel-method-unsupported-default")]
            public sealed partial class HelperKernel
            {
                public int Run(HookContext ctx)
                {
                    return UsesUnsupportedDefault();
                }

                [KernelMethod]
                public static int UsesUnsupportedDefault(object? value = null) => 1;
            }
            """);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("supported sandbox type", StringComparison.Ordinal));
    }

    private static SandboxPolicy PurePolicy()
        => SandboxPolicyBuilder.Create()
            .WithFuel(10_000)
            .WithMaxHostCalls(100)
            .WithWallTime(TimeSpan.FromSeconds(5))
            .Build();

    private static SandboxPolicy ProbeReadPolicy()
        => SandboxPolicyBuilder.Create()
            .Grant("probe.read.next", new { }, SandboxEffect.HostStateRead)
            .WithFuel(10_000)
            .WithMaxHostCalls(100)
            .WithWallTime(TimeSpan.FromSeconds(5))
            .Build();

    private static void AddNextBinding(SandboxHostBuilder builder, Func<int> next)
        => builder.AddBinding(new BindingDescriptor(
            "host.probe.next",
            SemVersion.One,
            [SandboxType.I32],
            SandboxType.I32,
            SandboxEffect.Cpu | SandboxEffect.HostStateRead,
            "probe.read.next",
            BindingCostModel.Fixed(1),
            AuditLevel.PerResource,
            BindingSafety.ReadOnlyExternal,
            (context, args, _) =>
            {
                var startedAt = DateTimeOffset.UtcNow;
                context.Audit.Write(new SandboxAuditEvent(
                    context.RunId,
                    "BindingCall",
                    startedAt,
                    true,
                    BindingId: "host.probe.next",
                    CapabilityId: "probe.read.next",
                    Effect: SandboxEffect.HostStateRead,
                    ResourceId: "probe:next",
                    Fields: context.BindingAuditFields("probe", startedAt)));
                return ValueTask.FromResult(SandboxValue.FromInt32(
                    Assert.IsType<I32Value>(args[0]).Value + next()));
            },
            CompiledBinding.RuntimeStub("DotBoxD.Kernels.Runtime.CompiledRuntime", "CallBinding"),
            GrantValidator: static (_, _) => { }));
}
