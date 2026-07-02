using System.Reflection;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionReceiverAuthorityTests
{
    private const string ScopedReceiverSource = """
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Services.Attributes;

        namespace Sample;

        [RpcService]
        public interface IRemoteMonster
        {
            string Id { get; }

            [HostBinding("game.world.monster.read.threat", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
            int Threat();
        }

        public sealed class RemoteMonster : IRemoteMonster, IServerExtensionClientAccessor
        {
            public RemoteMonster(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions, string id)
            {
                ServerExtensions = serverExtensions;
                Id = id;
            }

            public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }

            public string Id { get; }

            public int Threat() => throw new System.NotSupportedException();
        }

        [ServerExtension(typeof(IRemoteMonster), "threat")]
        public sealed partial class ThreatKernel
        {
            private readonly IRemoteMonster _monster;

            public ThreatKernel(IRemoteMonster monster) => _monster = monster;

            [ServerExtensionMethod]
            public int ReadThreat(HookContext ctx)
            {
                return _monster.Threat();
            }
        }

        public static class Probe
        {
            public static int ReadThreat(RemoteMonster monster) => monster.ReadThreat();
        }
        """;

    private const string ScopedReceiverPropertySource = """
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Services.Attributes;

        namespace Sample;

        [RpcService]
        public interface IRemoteMonster
        {
            string Id { get; }

            [HostBinding("game.world.monster.read.threat", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
            int Threat();
        }

        public sealed class RemoteMonster : IRemoteMonster, IServerExtensionClientAccessor
        {
            public RemoteMonster(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions, string id)
            {
                ServerExtensions = serverExtensions;
                Id = id;
            }

            public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }

            public string Id { get; }

            public int Threat() => throw new System.NotSupportedException();
        }

        [ServerExtension(typeof(IRemoteMonster), "threat")]
        public sealed partial class ThreatKernel
        {
            private IRemoteMonster Monster { get; }

            public ThreatKernel(IRemoteMonster monster) => Monster = monster;

            [ServerExtensionMethod]
            public int ReadThreat(HookContext ctx)
            {
                return Monster.Threat();
            }
        }

        public static class Probe
        {
            public static int ReadThreat(RemoteMonster monster) => monster.ReadThreat();
        }
        """;

    private const string UnusedReceiverIdSource = """
        using DotBoxD.Abstractions;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Services.Attributes;

        namespace Sample;

        [RpcService]
        public interface IRemoteMonster
        {
            string Id { get; }
        }

        public sealed class RemoteMonster : IRemoteMonster, IServerExtensionClientAccessor
        {
            public RemoteMonster(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions, string id)
            {
                ServerExtensions = serverExtensions;
                Id = id;
            }

            public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }

            public string Id { get; }
        }

        [ServerExtension(typeof(IRemoteMonster), "echo")]
        public sealed partial class EchoKernel
        {
            [ServerExtensionMethod]
            public int Echo(int amount, HookContext ctx)
            {
                return amount;
            }
        }

        public static class Probe
        {
            public static int Echo(RemoteMonster monster, int amount) => monster.Echo(amount);
        }
        """;

    [Fact]
    public void Direct_graft_rejects_plugin_owned_receiver()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;

            namespace Sample;

            public sealed class PluginReceiver : IServerExtensionClientAccessor
            {
                public IServerExtensionClientRegistry ServerExtensions { get; } = null!;
            }

            [ServerExtension(typeof(PluginReceiver), "plugin-owned")]
            public sealed partial class BadKernel
            {
                [ServerExtensionMethod(typeof(PluginReceiver))]
                public int Run(HookContext ctx) => 0;
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("PluginReceiver", StringComparison.Ordinal) &&
                 d.GetMessage().Contains("server-owned [RpcService] interface", StringComparison.Ordinal));
    }

    [Fact]
    public void Scoped_receiver_field_uses_one_shared_receiver_id_argument()
    {
        var package = Package(ScopedReceiverSource, "Sample.ThreatPluginPackage");
        var parameter = Assert.Single(package.Module.Functions.Single().Parameters);
        Assert.Equal("__receiverId", parameter.Name);

        var registry = Invoke(ScopedReceiverSource, "ReadThreat", "monster-7", response: KernelRpcValue.Int32(12));

        var argument = Assert.Single(KernelRpcBinaryCodec.DecodeArguments(registry.LastArguments));
        Assert.Equal("monster-7", argument.StringValue);
    }

    [Fact]
    public void Scoped_receiver_property_uses_one_shared_receiver_id_argument()
    {
        var package = Package(ScopedReceiverPropertySource, "Sample.ThreatPluginPackage");
        var parameter = Assert.Single(package.Module.Functions.Single().Parameters);
        Assert.Equal("__receiverId", parameter.Name);

        var registry = Invoke(ScopedReceiverPropertySource, "ReadThreat", "monster-8", response: KernelRpcValue.Int32(13));

        var argument = Assert.Single(KernelRpcBinaryCodec.DecodeArguments(registry.LastArguments));
        Assert.Equal("monster-8", argument.StringValue);
    }

    [Fact]
    public void Receiver_id_property_without_injected_receiver_field_is_not_sent()
    {
        var package = Package(UnusedReceiverIdSource, "Sample.EchoPluginPackage");
        Assert.DoesNotContain(package.Module.Functions.Single().Parameters, p => p.Name == "__receiverId");

        var registry = Invoke(
            UnusedReceiverIdSource,
            "Echo",
            "monster-7",
            response: KernelRpcValue.Int32(9),
            9);

        var argument = Assert.Single(KernelRpcBinaryCodec.DecodeArguments(registry.LastArguments));
        Assert.Equal(9, argument.Int32Value);
    }

    private static PluginPackage Package(string source, string factoryType)
        => PluginAnalyzerGeneratedPackageFactory.Create(source, factoryType);

    private static RecordingRegistry Invoke(
        string source,
        string method,
        string receiverId,
        KernelRpcValue response,
        params object[] arguments)
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(source);
        var registry = new RecordingRegistry(KernelRpcBinaryCodec.EncodeValue(response));
        var receiverType = assembly.GetType("Sample.RemoteMonster", throwOnError: true)!;
        var receiver = Activator.CreateInstance(receiverType, [registry, receiverId])!;
        assembly.GetType("Sample.Probe", throwOnError: true)!
            .GetMethod(method, BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [receiver, .. arguments]);
        return registry;
    }

    private sealed class RecordingRegistry(byte[] response) : DotBoxD.Plugins.IServerExtensionClientRegistry
    {
        public byte[] LastArguments { get; private set; } = [];

        public string PluginId<TService>()
            where TService : class
            => typeof(TService).Name;

        public ValueTask<byte[]> InvokeServerExtensionAsync(
            string pluginId,
            byte[] arguments,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastArguments = arguments;
            return ValueTask.FromResult(response);
        }
    }
}
