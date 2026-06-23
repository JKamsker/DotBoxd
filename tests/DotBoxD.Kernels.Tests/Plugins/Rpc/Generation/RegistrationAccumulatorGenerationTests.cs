using System.Reflection;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class RegistrationAccumulatorGenerationTests
{
    private const string AccumulatorSource = """
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;

        namespace Sample;

        [GeneratePluginRegistrationAccumulator("ServiceRegistrationAccumulator", "Replace")]
        internal sealed class RemoteServiceControl
        {
            public List<string> Calls { get; } = [];

            public ValueTask<string> Replace<TService, TKernel>()
                where TService : class
                where TKernel : class, TService
            {
                Calls.Add("replace:" + typeof(TService).Name + ":" + typeof(TKernel).Name);
                return ValueTask.FromResult("service");
            }
        }

        [GeneratePluginRegistrationRootAccumulator("WorldRegistrationAccumulator")]
        internal sealed class RemoteWorldControl
        {
            public RemoteMonsterControl Monsters { get; } = new();

            public RemoteEntityControl Entities { get; } = new();
        }

        [GeneratePluginRegistrationAccumulator("RemoteMonsterExtensionAccumulator", "Extend")]
        internal sealed class RemoteMonsterControl
        {
            public List<string> Calls { get; } = [];

            public ValueTask<string> Extend<TService, TKernel>()
                where TService : class
                where TKernel : class
            {
                Calls.Add("extend:" + typeof(TService).Name + ":" + typeof(TKernel).Name);
                return ValueTask.FromResult("extension");
            }
        }

        internal sealed class RemoteEntityControl
        {
        }

        public interface IMonsterService
        {
        }

        public sealed class MonsterKernel : IMonsterService
        {
        }

        public sealed class MonsterExtensionKernel
        {
        }

        public static class Probe
        {
            public static async Task<string[]> RunAsync()
            {
                var services = new RemoteServiceControl();
                var serviceAccumulator = new ServiceRegistrationAccumulator(services)
                    .Replace<IMonsterService, MonsterKernel>();
                await serviceAccumulator.FlushAsync();

                var world = new RemoteWorldControl();
                var worldAccumulator = new WorldRegistrationAccumulator(world);
                worldAccumulator.Monsters.Extend<IMonsterService, MonsterExtensionKernel>();
                await worldAccumulator.FlushAsync();

                return [services.Calls[0], world.Monsters.Calls[0]];
            }
        }
        """;

    [Fact]
    public async Task Generated_accumulators_queue_and_flush_configured_registration_methods()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(AccumulatorSource);
        var probe = assembly.GetType("Sample.Probe", throwOnError: true)!;

        var task = (Task<string[]>)probe.GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, null)!;
        var calls = await task;

        Assert.Equal(
            ["replace:IMonsterService:MonsterKernel", "extend:IMonsterService:MonsterExtensionKernel"],
            calls);
    }

    [Fact]
    public void Generated_root_accumulator_exposes_only_annotated_child_controls()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(AccumulatorSource);
        var root = assembly.GetType("Sample.WorldRegistrationAccumulator", throwOnError: true)!;

        Assert.NotNull(root.GetProperty("Monsters", BindingFlags.Public | BindingFlags.Instance));
        Assert.Null(root.GetProperty("Entities", BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void Root_without_generated_child_accumulator_reports_diagnostic()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using DotBoxD.Abstractions;

            namespace Sample;

            [GeneratePluginRegistrationRootAccumulator("WorldRegistrationAccumulator")]
            internal sealed class RemoteWorldControl
            {
                public RemoteEntityControl Entities { get; } = new();
            }

            internal sealed class RemoteEntityControl
            {
            }
            """);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("has no public child control property", StringComparison.Ordinal));
    }
}
