using System.Reflection;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionClientExtensionInterfaceTests
{
    private const string ExplicitInterfaceReceiverSource = """
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace Sample;

        public interface IServerExtensionsBacked
        {
            DotBoxD.Plugins.IServerExtensionClientRegistry ServerExtensions { get; }
        }

        public sealed class RemoteMonsterControl : IServerExtensionsBacked
        {
            private readonly DotBoxD.Plugins.IServerExtensionClientRegistry _kernelRpc;

            public RemoteMonsterControl(DotBoxD.Plugins.IServerExtensionClientRegistry kernelRpc) => _kernelRpc = kernelRpc;

            DotBoxD.Plugins.IServerExtensionClientRegistry IServerExtensionsBacked.ServerExtensions => _kernelRpc;
        }

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

        [ServerExtensionClient(typeof(RemoteMonsterControl))]
        [ServerExtension("monster-killer", typeof(IMonsterKillerService))]
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

        public static class Probe
        {
            public static IMonsterKillerService Service(RemoteMonsterControl control) => control.MonsterKiller;
        }
        """;

    [Fact]
    public async Task Generated_domain_extension_uses_explicit_interface_kernel_rpc_property()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(ExplicitInterfaceReceiverSource);
        var controlType = assembly.GetType("Sample.RemoteMonsterControl", throwOnError: true)!;
        var probeType = assembly.GetType("Sample.Probe", throwOnError: true)!;
        var registry = new RecordingServerExtensionsRegistry(KillResultsResponse());
        var control = Activator.CreateInstance(controlType, [registry])!;

        var service = probeType.GetMethod("Service", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [control]);
        Assert.NotNull(service);
        var method = service!.GetType().GetMethod("KillMonstersAsync")!;
        var valueTask = method.Invoke(service, [new List<int> { 4 }])!;

        var result = await AwaitValueTaskResult(valueTask);

        Assert.Equal("monster-killer", registry.LastPluginId);
        Assert.Equal("Sample.IMonsterKillerService", registry.LastServiceType);
        var values = Assert.IsAssignableFrom<System.Collections.IEnumerable>(result).Cast<object>().ToArray();
        Assert.Single(values);
        AssertGeneratedKillResult(values[0], 4, true);
    }

    [Fact]
    public void Generated_domain_extension_reports_inaccessible_explicit_interface_kernel_rpc_property()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using System.Threading.Tasks;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public interface IMonsterKillerService
            {
                ValueTask<int> KillAsync(int monsterId);
            }

            public sealed class Owner
            {
                private interface IServerExtensionsBacked
                {
                    DotBoxD.Plugins.IServerExtensionClientRegistry ServerExtensions { get; }
                }

                public sealed class RemoteMonsterControl : IServerExtensionsBacked
                {
                    DotBoxD.Plugins.IServerExtensionClientRegistry IServerExtensionsBacked.ServerExtensions => null!;
                }

                [ServerExtensionClient(typeof(RemoteMonsterControl))]
                [ServerExtension("monster-killer", typeof(IMonsterKillerService))]
                public sealed partial class MonsterKillerKernel
                {
                    public int Kill(int monsterId, HookContext ctx)
                    {
                        return monsterId;
                    }
                }
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("ServerExtensions property", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => d.Id == "CS0122");
    }

    private static byte[] KillResultsResponse()
        => KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.List(
        [
            KernelRpcValue.Record([KernelRpcValue.Int32(4), KernelRpcValue.Bool(true)])
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

    private sealed class RecordingServerExtensionsRegistry(byte[] response) : DotBoxD.Plugins.IServerExtensionClientRegistry
    {
        public string? LastPluginId { get; private set; }
        public string? LastServiceType { get; private set; }

        public string PluginId<TService>() where TService : class
        {
            LastServiceType = typeof(TService).FullName;
            return "monster-killer";
        }

        public ValueTask<byte[]> InvokeServerExtensionAsync(
            string pluginId,
            byte[] arguments,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastPluginId = pluginId;
            return ValueTask.FromResult(response);
        }
    }
}
