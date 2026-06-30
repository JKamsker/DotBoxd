using System.Reflection;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class RegistrationAccumulatorInheritedChildTests
{
    [Fact]
    public async Task Generated_root_accumulator_includes_inherited_public_child_controls()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly("""
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;

            namespace Sample;

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

            internal class RemoteWorldControlBase
            {
                public RemoteMonsterControl Monsters { get; } = new();
            }

            [GeneratePluginRegistrationRootAccumulator("WorldRegistrationAccumulator")]
            internal sealed class RemoteWorldControl : RemoteWorldControlBase
            {
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

        Assert.Equal("extend:IMonsterService:MonsterExtensionKernel", call);
    }

    [Fact]
    public async Task Generated_root_accumulator_uses_inherited_child_when_derived_field_shadows_name()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly("""
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;

            namespace Sample;

            [GeneratePluginRegistrationAccumulator("RemoteMonsterExtensionAccumulator", "Extend")]
            internal sealed class RemoteMonsterControl(string name)
            {
                public List<string> Calls { get; } = [];

                public ValueTask<string> Extend<TService, TKernel>()
                    where TService : class
                    where TKernel : class
                {
                    Calls.Add(name);
                    return ValueTask.FromResult("extension");
                }
            }

            internal class RemoteWorldControlBase
            {
                public RemoteMonsterControl Monsters { get; } = new("base-property");
            }

            [GeneratePluginRegistrationRootAccumulator("WorldRegistrationAccumulator")]
            internal sealed class RemoteWorldControl : RemoteWorldControlBase
            {
                public new RemoteMonsterControl Monsters = new("derived-field");
            }

            public interface IMonsterService
            {
            }

            public sealed class MonsterExtensionKernel
            {
            }

            public static class Probe
            {
                public static async Task<string[]> RunAsync()
                {
                    var world = new RemoteWorldControl();
                    var accumulator = new WorldRegistrationAccumulator(world);
                    accumulator.Monsters.Extend<IMonsterService, MonsterExtensionKernel>();
                    await accumulator.FlushAsync();
                    return [
                        ((RemoteWorldControlBase)world).Monsters.Calls.Count == 0
                            ? "<none>"
                            : ((RemoteWorldControlBase)world).Monsters.Calls[0],
                        world.Monsters.Calls.Count == 0 ? "<none>" : world.Monsters.Calls[0]
                    ];
                }
            }
            """);
        var probe = assembly.GetType("Sample.Probe", throwOnError: true)!;

        var task = (Task<string[]>)probe.GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, null)!;
        var calls = await task;

        Assert.Equal(["base-property", "<none>"], calls);
    }

    [Fact]
    public async Task Generated_root_accumulator_keeps_inherited_child_when_private_property_shadows_name()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly("""
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;

            namespace Sample;

            [GeneratePluginRegistrationAccumulator("RemoteMonsterExtensionAccumulator", "Extend")]
            internal sealed class RemoteMonsterControl(string name)
            {
                public List<string> Calls { get; } = [];

                public ValueTask<string> Extend<TService, TKernel>()
                    where TService : class
                    where TKernel : class
                {
                    Calls.Add(name);
                    return ValueTask.FromResult("extension");
                }
            }

            internal class RemoteWorldControlBase
            {
                public RemoteMonsterControl Monsters { get; } = new("base-property");
            }

            [GeneratePluginRegistrationRootAccumulator("WorldRegistrationAccumulator")]
            internal sealed class RemoteWorldControl : RemoteWorldControlBase
            {
                private new RemoteMonsterControl Monsters { get; } = new("private-property");
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
                    return ((RemoteWorldControlBase)world).Monsters.Calls[0];
                }
            }
            """);
        var probe = assembly.GetType("Sample.Probe", throwOnError: true)!;

        var task = (Task<string>)probe.GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, null)!;
        var call = await task;

        Assert.Equal("base-property", call);
    }
}
