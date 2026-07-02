using System.Reflection;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed partial class ServerExtensionClientContractSurpriseTests
{
    private const string CancellationTokenClientSource = """
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Services.Attributes;
        using DotBoxD.Abstractions;

        namespace Sample;

        [RpcService]
        public interface IRemoteControl
        {
        }

        public sealed class RemoteControl : IRemoteControl, IServerExtensionClientAccessor
        {
            public RemoteControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions)
                => ServerExtensions = serverExtensions;

            public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
        }

        public interface ICounterService
        {
            ValueTask<int> CountAsync(int id, CancellationToken cancellationToken = default);
        }

        [ServerExtensionClient(typeof(IRemoteControl))]
        [ServerExtension("counter", typeof(ICounterService))]
        public sealed partial class CounterKernel
        {
            public int Count(int id, HookContext ctx) => id;
        }

        public static class Probe
        {
            public static ValueTask<int> Count(RemoteControl control, int id, CancellationToken cancellationToken)
                => control.Counter.CountAsync(id, cancellationToken);
        }
        """;

    private const string DirectCancellationTokenClientSource = """
        using System.Threading;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Services.Attributes;
        using DotBoxD.Abstractions;

        namespace Sample;

        [RpcService]
        public interface IRemoteControl
        {
        }

        public sealed class RemoteControl : IRemoteControl, IServerExtensionClientAccessor
        {
            public RemoteControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions)
                => ServerExtensions = serverExtensions;

            public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
        }

        [ServerExtension(typeof(IRemoteControl), "direct-counter")]
        public sealed partial class CounterKernel
        {
            [ServerExtensionMethod(typeof(IRemoteControl))]
            public int Count(int id, HookContext ctx) => id;
        }

        public static class Probe
        {
            public static int Count(RemoteControl control, int id, CancellationToken cancellationToken)
                => control.Count(id, cancellationToken);
        }
        """;

    private const string DiamondInheritedClientSource = """
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Services.Attributes;
        using DotBoxD.Abstractions;

        namespace Sample;

        [RpcService]
        public interface IRemoteControl
        {
        }

        public sealed class RemoteControl : IRemoteControl, IServerExtensionClientAccessor
        {
            public RemoteControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions)
                => ServerExtensions = serverExtensions;

            public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
        }

        public interface ILeftCounterService
        {
            ValueTask<int> CountAsync(int id, CancellationToken cancellationToken = default);
        }

        public interface IRightCounterService
        {
            ValueTask<int> CountAsync(int id, CancellationToken cancellationToken = default);
        }

        public interface ICounterService : ILeftCounterService, IRightCounterService
        {
        }

        [ServerExtensionClient(typeof(IRemoteControl))]
        [ServerExtension("counter", typeof(ICounterService))]
        public sealed partial class CounterKernel
        {
            public int Count(int id, HookContext ctx) => id;
        }

        public static class Probe
        {
            public static ValueTask<int> Count(RemoteControl control, int id, CancellationToken cancellationToken)
                => control.Counter.CountAsync(id, cancellationToken);
        }
        """;

    [Fact]
    public async Task Generated_client_forwards_service_cancellation_tokens()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(CancellationTokenClientSource);
        var registry = new RecordingRegistry(
            "counter",
            KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Int32(42)));
        var controlType = assembly.GetType("Sample.RemoteControl", throwOnError: true)!;
        var control = Activator.CreateInstance(controlType, [registry])!;
        using var cts = new CancellationTokenSource();

        var valueTask = assembly.GetType("Sample.Probe", throwOnError: true)!
            .GetMethod("Count", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [control, 7, cts.Token])!;
        var result = await AwaitValueTaskResult(valueTask);

        Assert.Equal(42, result);
        Assert.Equal(cts.Token, registry.LastCancellationToken);
        var arguments = KernelRpcBinaryCodec.DecodeArguments(registry.LastArguments);
        var argument = Assert.Single(arguments);
        Assert.Equal(7, argument.Int32Value);
    }

    [Fact]
    public void Generated_direct_receiver_client_forwards_cancellation_tokens()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(DirectCancellationTokenClientSource);
        var registry = new RecordingRegistry(
            "direct-counter",
            KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Int32(42)));
        var controlType = assembly.GetType("Sample.RemoteControl", throwOnError: true)!;
        var control = Activator.CreateInstance(controlType, [registry])!;
        using var cts = new CancellationTokenSource();

        var result = assembly.GetType("Sample.Probe", throwOnError: true)!
            .GetMethod("Count", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [control, 7, cts.Token]);

        Assert.Equal(42, result);
        Assert.Equal(cts.Token, registry.LastCancellationToken);
        var arguments = KernelRpcBinaryCodec.DecodeArguments(registry.LastArguments);
        var argument = Assert.Single(arguments);
        Assert.Equal(7, argument.Int32Value);
    }

    [Fact]
    public void Generated_client_rejects_generic_service_methods_with_DBXK100()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using System.Threading.Tasks;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public interface IPingService
            {
                ValueTask PingAsync<T>();
            }

            [ServerExtension("ping", typeof(IPingService))]
            public sealed partial class PingKernel
            {
                public void Ping(HookContext ctx)
                {
                }
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("generic", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(diagnostics, d => d.Id == "CS0305");
    }

    [Fact]
    public async Task Generated_client_accepts_a_diamond_inherited_single_method_contract()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(DiamondInheritedClientSource);
        var registry = new RecordingRegistry(
            "counter",
            KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Int32(42)));
        var controlType = assembly.GetType("Sample.RemoteControl", throwOnError: true)!;
        var control = Activator.CreateInstance(controlType, [registry])!;
        using var cts = new CancellationTokenSource();

        var valueTask = assembly.GetType("Sample.Probe", throwOnError: true)!
            .GetMethod("Count", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [control, 7, cts.Token])!;
        var result = await AwaitValueTaskResult(valueTask);

        Assert.Equal(42, result);
        Assert.Equal(cts.Token, registry.LastCancellationToken);
        var arguments = KernelRpcBinaryCodec.DecodeArguments(registry.LastArguments);
        var argument = Assert.Single(arguments);
        Assert.Equal(7, argument.Int32Value);
    }

    private static async Task<object?> AwaitValueTaskResult(object valueTask)
    {
        var asTask = valueTask.GetType().GetMethod("AsTask", Type.EmptyTypes)!;
        var task = (Task)asTask.Invoke(valueTask, null)!;
        await task.ConfigureAwait(false);
        return task.GetType().GetProperty("Result")!.GetValue(task);
    }

    private sealed class RecordingRegistry(string expectedPluginId, byte[] response)
        : DotBoxD.Plugins.IServerExtensionClientRegistry
    {
        public byte[] LastArguments { get; private set; } = [];

        public CancellationToken LastCancellationToken { get; private set; }

        public string PluginId<TService>()
            where TService : class
            => expectedPluginId;

        public ValueTask<byte[]> InvokeServerExtensionAsync(
            string pluginId,
            byte[] arguments,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(expectedPluginId, pluginId);
            LastArguments = arguments;
            LastCancellationToken = cancellationToken;
            return ValueTask.FromResult(response);
        }
    }
}
