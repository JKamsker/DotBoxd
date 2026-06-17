using System.Reflection;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

/// <summary>The client-facing contract for the batch kernel; its return type drives result marshaling.</summary>
public interface IMonsterKillerService
{
    List<KillResult> KillMonsters(List<int> monsterIds);
}

/// <summary>A complex result object returned per monster — proves DTOs and lists of DTOs marshal back.</summary>
public readonly record struct KillResult(int MonsterId, bool Success);

/// <summary>Marker kernel type; <see cref="BatchKillerPluginPackage"/> is its generated-shaped package.</summary>
public sealed class BatchKillerKernel;

/// <summary>Stands in for the generator output so <c>KernelPackageRegistry.Resolve</c> finds it by name.</summary>
public static class BatchKillerPluginPackage
{
    public static PluginPackage Create() => RpcKernelTestPackages.MonsterKiller();
}

/// <summary>
/// Proves the typed server extension surface (Followup #2.4): a service interface is implemented by a
/// runtime proxy that marshals C# arguments into the sandbox, runs the batch kernel request/response in
/// one roundtrip, and marshals the result — including a <c>List&lt;KillResult&gt;</c> of DTOs — back to
/// real C# objects. Covers both the proxy over a generated kernel and the
/// <c>RegisterServerExtensionAsync</c>/<c>ServerExtension</c> registration sugar, plus the marshaller round-trips.
/// </summary>
public sealed class ServerExtensionProxyTests
{
    private const string MonsterKillerSource = """
        using System.Collections.Generic;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace Sample;

        public interface IGameWorld
        {
            [HostBinding("host.world.kill", "game.world.monster.write.kill", SandboxEffect.Cpu | SandboxEffect.HostStateWrite)]
            bool Kill(int id);
        }

        public readonly record struct KillResult(int MonsterId, bool Success);

        [ServerExtension("monster-killer")]
        public sealed partial class MonsterKillerKernel
        {
            public List<KillResult> KillMonsters(List<int> monsterIds, HookContext ctx)
            {
                var results = new List<KillResult>();
                foreach (var id in monsterIds)
                    results.Add(new KillResult(id, ctx.Host<IGameWorld>().Kill(id)));
                return results;
            }
        }
        """;

    private const string MonsterKillerWithGeneratedClientSource = """
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace Sample;

        public interface IMonsterKillerService
        {
            ValueTask<List<KillResult>> KillMonstersAsync(List<int> monsterIds);
        }

        public interface IGameWorld
        {
            [HostBinding("host.world.kill", "game.world.monster.write.kill", SandboxEffect.Cpu | SandboxEffect.HostStateWrite)]
            bool Kill(int id);
        }

        public readonly record struct KillResult(int MonsterId, bool Success);

        [ServerExtension("monster-killer", typeof(IMonsterKillerService))]
        public sealed partial class MonsterKillerKernel
        {
            public async ValueTask<List<KillResult>> KillMonsters(List<int> monsterIds, HookContext ctx)
            {
                var results = new List<KillResult>();
                foreach (var id in monsterIds)
                    results.Add(new KillResult(id, ctx.Host<IGameWorld>().Kill(id)));
                return results;
            }
        }
        """;

    [Fact]
    public async Task A_typed_proxy_over_a_generated_kernel_returns_dtos_as_real_objects()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(MonsterKillerSource, "Sample.MonsterKillerPluginPackage");
        using var server = DotBoxD.Plugins.PluginServer.Create(configureHost: RpcKernelTestPackages.AddKillBinding, defaultPolicy: RpcKernelTestPackages.KillPolicy());
        var kernel = await server.InstallServerExtensionAsync(package);

        var service = ServerExtensionProxy.Create<IMonsterKillerService>(kernel);
        var results = service.KillMonsters([4, 5, 6]);

        Assert.Equal(3, results.Count);
        Assert.Equal(new KillResult(4, true), results[0]);
        Assert.Equal(new KillResult(5, false), results[1]);
        Assert.Equal(new KillResult(6, true), results[2]);
    }

    [Fact]
    public async Task RegisterServerExtension_then_ServerExtension_invokes_the_batch_kernel_by_contract()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create(configureHost: RpcKernelTestPackages.AddKillBinding, defaultPolicy: RpcKernelTestPackages.KillPolicy());
        await server.RegisterServerExtensionAsync<IMonsterKillerService, BatchKillerKernel>();

        var results = server.ServerExtension<IMonsterKillerService>().KillMonsters([1, 2]);

        Assert.Equal([new KillResult(1, false), new KillResult(2, true)], results);
    }

    [Fact]
    public void Marshaller_round_trips_dtos_and_lists_of_dtos()
    {
        var original = new List<KillResult> { new(7, true), new(8, false) };
        var sandbox = KernelRpcMarshaller.ToSandboxValue(original, typeof(List<KillResult>));

        var list = Assert.IsType<ListValue>(sandbox);
        Assert.Equal(2, list.Values.Count);
        var firstRecord = Assert.IsType<RecordValue>(list.Values[0]);
        Assert.Equal([SandboxValue.FromInt32(7), SandboxValue.FromBool(true)], firstRecord.Fields);

        var roundTripped = (List<KillResult>)KernelRpcMarshaller.FromSandboxValue(sandbox, typeof(List<KillResult>))!;
        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public async Task A_generated_ipc_client_uses_compact_ir_without_dispatch_proxy()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(MonsterKillerWithGeneratedClientSource);
        var clientType = assembly.GetType("Sample.MonsterKillerKernelServerExtensionClient", throwOnError: true)!;
        Assert.False(typeof(DispatchProxy).IsAssignableFrom(clientType));

        var wireClient = new RecordingServerExtensionWireClient(KillResultsResponse());
        var create = clientType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!;
        var service = create.Invoke(null, [wireClient, "monster-killer"])!;
        var method = service.GetType().GetMethod("KillMonstersAsync", BindingFlags.Public | BindingFlags.Instance)!;

        var result = await AwaitValueTaskResult(
            method.Invoke(service, [new List<int> { 4, 5 }])!);

        Assert.Equal("monster-killer", wireClient.LastPluginId);
        var arguments = KernelRpcBinaryCodec.DecodeArguments(wireClient.LastArguments);
        var listArgument = Assert.Single(arguments);
        listArgument.RequireKind(KernelRpcValueKind.List);
        Assert.Equal(2, listArgument.Items.Length);
        Assert.Equal(4, listArgument.Items[0].Int32Value);
        Assert.Equal(5, listArgument.Items[1].Int32Value);
        Assert.True(wireClient.LastArguments.Length < 32);

        var results = Assert.IsAssignableFrom<System.Collections.IEnumerable>(result).Cast<object>().ToArray();
        Assert.Equal(2, results.Length);
        AssertGeneratedKillResult(results[0], 4, true);
        AssertGeneratedKillResult(results[1], 5, false);
    }

    [Fact]
    public void Generated_client_rejects_service_parameter_modifier_mismatch()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using System.Threading.Tasks;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public interface IEchoService
            {
                ValueTask<int> EchoAsync(int value);
            }

            [ServerExtension("echo", typeof(IEchoService))]
            public sealed partial class EchoKernel
            {
                public int Echo(ref int value, HookContext ctx) => value;
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("cannot use ref, in, or out", StringComparison.Ordinal));
    }

    [Fact]
    public void Generated_client_rejects_dto_constructor_parameter_type_mismatch()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using System.Threading.Tasks;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed class KillResult
            {
                public KillResult(string monsterId, bool success)
                {
                    MonsterId = monsterId.Length;
                    Success = success;
                }

                public int MonsterId { get; }
                public bool Success { get; }
            }

            public interface IKillService
            {
                ValueTask<KillResult> KillAsync(int monsterId);
            }

            [ServerExtension("kill", typeof(IKillService))]
            public sealed partial class KillKernel
            {
                public KillResult Kill(int monsterId, HookContext ctx)
                {
                    return new KillResult("monster", true);
                }
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("constructor matching its public fields", StringComparison.Ordinal));
    }

    private static byte[] KillResultsResponse()
        => KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.List(
        [
            KernelRpcValue.Record([KernelRpcValue.Int32(4), KernelRpcValue.Bool(true)]),
            KernelRpcValue.Record([KernelRpcValue.Int32(5), KernelRpcValue.Bool(false)])
        ]));

    private static async Task<object?> AwaitValueTaskResult(object valueTask)
    {
        var asTask = valueTask.GetType().GetMethod("AsTask", Type.EmptyTypes)!;
        var task = (Task)asTask.Invoke(valueTask, null)!;
        await task.ConfigureAwait(false);
        return task.GetType().GetProperty("Result")!.GetValue(task);
    }

    private static void AssertGeneratedKillResult(object result, int monsterId, bool success)
    {
        var type = result.GetType();
        Assert.Equal(monsterId, type.GetProperty("MonsterId")!.GetValue(result));
        Assert.Equal(success, type.GetProperty("Success")!.GetValue(result));
    }

    private sealed class RecordingServerExtensionWireClient : DotBoxD.Plugins.IServerExtensionWireClient
    {
        private readonly byte[] _response;

        public RecordingServerExtensionWireClient(byte[] response) => _response = response;

        public string? LastPluginId { get; private set; }

        public byte[] LastArguments { get; private set; } = [];

        public ValueTask<byte[]> InvokeServerExtensionAsync(
            string pluginId,
            byte[] arguments,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastPluginId = pluginId;
            LastArguments = arguments;
            return ValueTask.FromResult(_response);
        }
    }
}
