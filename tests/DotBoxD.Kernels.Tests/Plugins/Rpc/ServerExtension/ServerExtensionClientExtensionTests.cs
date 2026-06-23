using System.Reflection;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionClientExtensionTests
{
    private const string DomainExtensionSource = """
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace Sample;

        public sealed class RemoteMonsterControl
        {
            public RemoteMonsterControl(DotBoxD.Plugins.IServerExtensionClientRegistry serverExtensions) => ServerExtensions = serverExtensions;
            public DotBoxD.Plugins.IServerExtensionClientRegistry ServerExtensions { get; }
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
            [ServerExtensionMethod(typeof(RemoteMonsterControl))]
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

            public static ValueTask<List<KillResult>> Kill(RemoteMonsterControl control, List<int> monsterIds)
                => control.KillMonsters(monsterIds);
        }
        """;

    internal const string DirectSyncExtensionSource = """
        using System.Collections.Generic;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace Sample;

        public sealed class RemoteMonsterControl : IServerExtensionClientAccessor
        {
            public RemoteMonsterControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions) => ServerExtensions = serverExtensions;
            public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
        }

        public interface IGameWorld
        {
            [HostBinding("host.world.kill", "game.world.monster.write.kill", SandboxEffect.Cpu | SandboxEffect.HostStateWrite)]
            bool Kill(int id);
        }

        public readonly record struct KillResult(int MonsterId, bool Success);

        [ServerExtension(typeof(RemoteMonsterControl), "monster-killer")]
        public sealed partial class MonsterKillerKernel
        {
            [ServerExtensionMethod(typeof(RemoteMonsterControl))]
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
            public static List<KillResult> Kill(RemoteMonsterControl control, List<int> monsterIds)
                => control.KillMonsters(monsterIds);
        }
        """;

    [Fact]
    public async Task Generated_domain_extensions_resolve_registered_server_extension_client()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(DomainExtensionSource);
        var controlType = assembly.GetType("Sample.RemoteMonsterControl", throwOnError: true)!;
        var probeType = assembly.GetType("Sample.Probe", throwOnError: true)!;
        var registry = new RecordingServerExtensionsRegistry(KillResultsResponse());
        var control = Activator.CreateInstance(controlType, [registry])!;

        var service = probeType.GetMethod("Service", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [control]);
        Assert.NotNull(service);

        var valueTask = probeType.GetMethod("Kill", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [control, new List<int> { 4, 5 }])!;
        var result = await AwaitValueTaskResult(valueTask);

        Assert.Equal("monster-killer", registry.LastPluginId);
        Assert.Equal("Sample.IMonsterKillerService", registry.LastServiceType);
        var arguments = KernelRpcBinaryCodec.DecodeArguments(registry.LastArguments);
        Assert.Equal([4, 5], arguments[0].Items.Select(item => item.Int32Value));

        var results = Assert.IsAssignableFrom<System.Collections.IEnumerable>(result).Cast<object>().ToArray();
        Assert.Equal(2, results.Length);
        AssertGeneratedKillResult(results[0], 4, true);
        AssertGeneratedKillResult(results[1], 5, false);
    }

    [Fact]
    public void Generated_direct_extensions_support_synchronous_kernel_returns()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(DirectSyncExtensionSource);
        var controlType = assembly.GetType("Sample.RemoteMonsterControl", throwOnError: true)!;
        var probeType = assembly.GetType("Sample.Probe", throwOnError: true)!;
        var registry = new RecordingServerExtensionsRegistry(KillResultsResponse());
        var control = Activator.CreateInstance(controlType, [registry])!;

        var result = probeType.GetMethod("Kill", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [control, new List<int> { 4, 5 }])!;

        Assert.Equal("monster-killer", registry.LastPluginId);
        Assert.Equal("Sample.MonsterKillerKernel", registry.LastServiceType);
        var results = Assert.IsAssignableFrom<System.Collections.IEnumerable>(result).Cast<object>().ToArray();
        Assert.Equal(2, results.Length);
        AssertGeneratedKillResult(results[0], 4, true);
        AssertGeneratedKillResult(results[1], 5, false);
    }

    [Fact]
    public void Generated_property_reports_conflict_with_receiver_member()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(
            ConflictSource(
                "public int MonsterKiller => 0;",
                "[ServerExtensionClient(typeof(RemoteMonsterControl))]",
                string.Empty));

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("property 'MonsterKiller'", StringComparison.Ordinal));
    }

    [Fact]
    public void Generated_method_reports_conflict_with_receiver_member()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(
            ConflictSource(
                "public void KillMonsters() { }",
                string.Empty,
                "[ServerExtensionMethod(typeof(RemoteMonsterControl))]"));

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("method 'KillMonsters'", StringComparison.Ordinal));
    }

    private static string ConflictSource(string receiverMember, string classAttributes, string methodAttributes)
        => $$"""
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed class RemoteMonsterControl
            {
                public DotBoxD.Plugins.IServerExtensionClientRegistry ServerExtensions { get; } = null!;
                {{receiverMember}}
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

            {{classAttributes}}
            [ServerExtension("monster-killer", typeof(IMonsterKillerService))]
            public sealed partial class MonsterKillerKernel
            {
                {{methodAttributes}}
                public List<KillResult> KillMonsters(List<int> monsterIds, HookContext ctx)
                {
                    var results = new List<KillResult>();
                    foreach (var id in monsterIds)
                        results.Add(new KillResult(id, ctx.Host<IGameWorld>().Kill(id)));
                    return results;
                }
            }
            """;

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

    private sealed class RecordingServerExtensionsRegistry(byte[] response) : DotBoxD.Plugins.IServerExtensionClientRegistry
    {
        public string? LastPluginId { get; private set; }

        public string? LastServiceType { get; private set; }

        public byte[] LastArguments { get; private set; } = [];

        public string PluginId<TService>()
            where TService : class
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
            LastArguments = arguments;
            return ValueTask.FromResult(response);
        }
    }
}
