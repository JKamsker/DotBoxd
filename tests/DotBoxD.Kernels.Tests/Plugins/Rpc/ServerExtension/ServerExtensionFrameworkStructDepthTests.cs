using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionFrameworkStructDepthTests
{
    [Fact]
    public void Generated_server_extension_rejects_Range_when_record_shape_exceeds_depth_limit()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using System;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record Level0(Level1 Value);
            public sealed record Level1(Level2 Value);
            public sealed record Level2(Level3 Value);
            public sealed record Level3(Level4 Value);
            public sealed record Level4(Level5 Value);
            public sealed record Level5(Level6 Value);
            public sealed record Level6(Range Value);

            [ServerExtension("deep-range")]
            public sealed partial class DeepRangeKernel
            {
                public Level0 Echo(Level0 value, HookContext ctx) => value;
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("depth", StringComparison.OrdinalIgnoreCase));
    }
}
