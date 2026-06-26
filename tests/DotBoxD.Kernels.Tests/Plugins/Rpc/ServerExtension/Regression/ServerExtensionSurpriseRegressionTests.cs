using System.Reflection;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed partial class ServerExtensionSurpriseRegressionTests
{
    [Fact]
    public void Direct_extension_honors_method_level_receiver_and_name()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(DirectMethodMetadataSource);
        var controlType = assembly.GetType("Sample.RemoteMonsterControl", throwOnError: true)!;
        var probeType = assembly.GetType("Sample.Probe", throwOnError: true)!;
        var registry = new RecordingServerExtensionsRegistry(
            "renamed",
            KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Int32(9)));
        var control = Activator.CreateInstance(controlType, [registry])!;

        var result = probeType.GetMethod("Kill", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [control]);

        Assert.Equal(9, result);
        Assert.Equal("renamed", registry.LastPluginId);
        Assert.Equal("Sample.RenameKernel", registry.LastServiceType);
        var arguments = KernelRpcBinaryCodec.DecodeArguments(registry.LastArguments);
        Assert.Equal(4, arguments[0].Int32Value);
    }

    [Theory]
    [InlineData("void", "")]
    [InlineData("Task", "async")]
    [InlineData("ValueTask", "async")]
    public void No_payload_server_extension_returns_generate_Unit_return_type(
        string returnType,
        string modifier)
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            NoPayloadSource(returnType, modifier),
            "Sample.PingPluginPackage");

        Assert.Equal(SandboxType.Unit, Assert.Single(package.Module.Functions).ReturnType);
    }

    [Theory]
    [InlineData("void", "")]
    [InlineData("Task", "async")]
    [InlineData("ValueTask", "async")]
    public async Task Direct_extension_supports_non_generic_no_payload_return(
        string returnType,
        string modifier)
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(
            NoPayloadSource(returnType, modifier, includeProbe: true));
        var controlType = assembly.GetType("Sample.RemoteMonsterControl", throwOnError: true)!;
        var probeType = assembly.GetType("Sample.Probe", throwOnError: true)!;
        var registry = new RecordingServerExtensionsRegistry(
            "ping",
            KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Unit()));
        var control = Activator.CreateInstance(controlType, [registry])!;

        var result = probeType.GetMethod("Ping", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [control]);
        await AwaitNoPayload(result);

        Assert.Equal("ping", registry.LastPluginId);
        Assert.Equal("Sample.PingKernel", registry.LastServiceType);
        var arguments = KernelRpcBinaryCodec.DecodeArguments(registry.LastArguments);
        Assert.Equal(4, arguments[0].Int32Value);
    }

    [Fact]
    public void Server_extension_rejects_nested_task_like_dto_member()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using System.Threading.Tasks;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record BadDto(Task<int> Count);

            [ServerExtension("bad-dto")]
            public sealed partial class BadKernel
            {
                public int Run(BadDto dto, HookContext ctx)
                {
                    return 1;
                }
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("top-level return type", StringComparison.Ordinal));
    }

    [Fact]
    public void Server_extension_discard_temp_does_not_collide_with_user_local()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            """
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public interface IGameWorld
            {
                [HostBinding("host.world.touch", "game.world.touch", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                int Touch();
            }

            [ServerExtension("discard-temp")]
            public sealed partial class DiscardKernel
            {
                public int Run(HookContext ctx)
                {
                    var __sir_discard0 = 7;
                    ctx.Host<IGameWorld>().Touch();
                    return __sir_discard0;
                }
            }
            """,
            "Sample.DiscardPluginPackage");
        var assignments = Assert.Single(package.Module.Functions).Body.OfType<AssignmentStatement>().ToArray();

        Assert.Contains(assignments, statement => statement.Name == "__sir_discard0");
        Assert.Contains(
            assignments,
            statement => statement.Name.StartsWith("__sir_discard", StringComparison.Ordinal) &&
                         statement.Name != "__sir_discard0");
    }

    private const string DirectMethodMetadataSource = """
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Services.Attributes;
        using DotBoxD.Abstractions;

        namespace Sample;

        [DotBoxDService]
        public interface IRemoteMonsterControl
        {
        }

        public sealed class RemoteMonsterControl : IRemoteMonsterControl, IServerExtensionClientAccessor
        {
            public RemoteMonsterControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions)
                => ServerExtensions = serverExtensions;

            public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
        }

        [ServerExtension(typeof(IRemoteMonsterControl), "renamed")]
        public sealed partial class RenameKernel
        {
            [ServerExtensionMethod(typeof(IRemoteMonsterControl), "KillNearby")]
            public int KillMonsters(int id, HookContext ctx)
            {
                return id;
            }
        }

        public static class Probe
        {
            public static int Kill(RemoteMonsterControl control) => control.KillNearby(4);
        }
        """;

    private static string NoPayloadSource(string returnType, string modifier, bool includeProbe = false)
    {
        var probe = includeProbe
            ? ProbeSource(returnType)
            : string.Empty;

        return $$"""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Services.Attributes;
            using DotBoxD.Abstractions;

            namespace Sample;

            [DotBoxDService]
            public interface IRemoteMonsterControl
            {
            }

            public sealed class RemoteMonsterControl : IRemoteMonsterControl, IServerExtensionClientAccessor
            {
                public RemoteMonsterControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions)
                    => ServerExtensions = serverExtensions;

                public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
            }

            public interface IGameWorld
            {
                [HostBinding("host.world.touch", "game.world.touch", SandboxEffect.Cpu | SandboxEffect.HostStateWrite)]
                void Touch(int id);
            }

            [ServerExtension(typeof(IRemoteMonsterControl), "ping")]
            public sealed partial class PingKernel
            {
                [ServerExtensionMethod]
                public {{modifier}} {{returnType}} Ping(int id, HookContext ctx)
                {
                    ctx.Host<IGameWorld>().Touch(id);
                }
            }
            {{probe}}
            """;
    }

    private static string ProbeSource(string returnType)
        => returnType == "void"
            ? """

            public static class Probe
            {
                public static void Ping(RemoteMonsterControl control) => control.Ping(4);
            }
            """
            : $$"""

            public static class Probe
            {
                public static {{returnType}} Ping(RemoteMonsterControl control) => control.Ping(4);
            }
            """;

    private static async Task AwaitNoPayload(object? result)
    {
        switch (result)
        {
            case null:
                return;
            case Task task:
                await task.ConfigureAwait(false);
                return;
            default:
                await AwaitValueTask(result).ConfigureAwait(false);
                return;
        }
    }

    private static async Task AwaitValueTask(object valueTask)
    {
        var asTask = valueTask.GetType().GetMethod("AsTask", Type.EmptyTypes)!;
        var task = (Task)asTask.Invoke(valueTask, null)!;
        await task.ConfigureAwait(false);
    }

    private sealed class RecordingServerExtensionsRegistry(
        string pluginId,
        byte[] response) : DotBoxD.Plugins.IServerExtensionClientRegistry
    {
        public string? LastPluginId { get; private set; }

        public string? LastServiceType { get; private set; }

        public byte[] LastArguments { get; private set; } = [];

        public string PluginId<TService>()
            where TService : class
        {
            LastServiceType = typeof(TService).FullName;
            return pluginId;
        }

        public ValueTask<byte[]> InvokeServerExtensionAsync(
            string pluginId,
            byte[] arguments,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastPluginId = pluginId;
            LastArguments = arguments;
            return ValueTask.FromResult(response);
        }
    }
}
