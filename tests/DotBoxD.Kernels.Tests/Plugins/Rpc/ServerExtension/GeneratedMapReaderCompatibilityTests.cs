using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class GeneratedMapReaderCompatibilityTests
{
    [Fact]
    public void Generated_map_readers_do_not_require_dictionary_TryAdd()
    {
        var generated = string.Join("\n", PluginAnalyzerGeneratedPackageFactory.GeneratedSources("""
            using System.Collections.Generic;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Services.Attributes;
            using DotBoxD.Abstractions;

            namespace Sample;

            [DotBoxDService]
            public interface IRemoteWorldControl;

            public sealed class RemoteWorldControl : IRemoteWorldControl, IServerExtensionClientAccessor
            {
                public RemoteWorldControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions)
                    => ServerExtensions = serverExtensions;

                public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
            }

            [ServerExtension(typeof(IRemoteWorldControl), "score-snapshot")]
            public sealed partial class ScoreSnapshotKernel
            {
                [ServerExtensionMethod(typeof(IRemoteWorldControl))]
                public Dictionary<string, int> Snapshot(Dictionary<string, int> scores, HookContext ctx)
                {
                    return scores;
                }
            }

            public static class Probe
            {
                public static Dictionary<string, int> Snapshot(
                    RemoteWorldControl control,
                    Dictionary<string, int> scores)
                    => control.Snapshot(scores);
            }
            """));

        Assert.DoesNotContain(".TryAdd(", generated, StringComparison.Ordinal);
        Assert.Contains(".ContainsKey(", generated, StringComparison.Ordinal);
        Assert.Contains(".Add(__key,", generated, StringComparison.Ordinal);
    }
}
