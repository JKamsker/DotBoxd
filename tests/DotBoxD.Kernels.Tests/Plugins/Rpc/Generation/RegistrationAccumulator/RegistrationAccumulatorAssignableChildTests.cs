using System.Reflection;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class RegistrationAccumulatorAssignableChildTests
{
    [Fact]
    public async Task Generated_root_accumulator_uses_base_accumulator_for_derived_child_property()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly("""
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;

            namespace Sample;

            [GeneratePluginRegistrationAccumulator("RemoteMonsterExtensionAccumulator", "Extend")]
            internal class RemoteMonsterControlBase
            {
                public List<string> Calls { get; } = [];

                public ValueTask<string> Extend<TService, TKernel>()
                    where TService : class
                    where TKernel : class
                {
                    Calls.Add("base-accumulator");
                    return ValueTask.FromResult("extension");
                }
            }

            internal sealed class RemoteMonsterControl : RemoteMonsterControlBase
            {
            }

            [GeneratePluginRegistrationRootAccumulator("WorldRegistrationAccumulator")]
            internal sealed class RemoteWorldControl
            {
                public RemoteMonsterControl Monsters { get; } = new();
            }

            public interface IMonsterService
            {
            }

            public sealed class MonsterExtensionKernel
            {
            }

            public static class Probe
            {
                public static async Task<string> RunAsync()
                {
                    var world = new RemoteWorldControl();
                    var accumulator = new WorldRegistrationAccumulator(world);
                    accumulator.Monsters.Extend<IMonsterService, MonsterExtensionKernel>();
                    await accumulator.FlushAsync();
                    return world.Monsters.Calls[0];
                }
            }
            """);
        var probe = assembly.GetType("Sample.Probe", throwOnError: true)!;

        var task = (Task<string>)probe.GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, null)!;
        var call = await task;

        Assert.Equal("base-accumulator", call);
    }

    [Fact]
    public void Generated_root_accumulator_rejects_ambiguous_assignable_child_accumulators()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;

            namespace Sample;

            [GeneratePluginRegistrationAccumulator("RemoteMonsterBaseAccumulator", "Extend")]
            internal class RemoteMonsterControlBase
            {
                public ValueTask<string> Extend<TService, TKernel>()
                    where TService : class
                    where TKernel : class
                    => ValueTask.FromResult("base");
            }

            [GeneratePluginRegistrationAccumulator("RemoteMonsterDerivedAccumulator", "Extend")]
            internal sealed class RemoteMonsterControl : RemoteMonsterControlBase
            {
                public new ValueTask<string> Extend<TService, TKernel>()
                    where TService : class
                    where TKernel : class
                    => ValueTask.FromResult("derived");
            }

            [GeneratePluginRegistrationRootAccumulator("WorldRegistrationAccumulator")]
            internal sealed class RemoteWorldControl
            {
                public RemoteMonsterControl Monsters { get; } = new();
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("Monsters", StringComparison.Ordinal) &&
                 d.GetMessage().Contains(
                     "matches more than one generated accumulator receiver",
                     StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => d.Id.StartsWith("CS", StringComparison.Ordinal));
    }
}
