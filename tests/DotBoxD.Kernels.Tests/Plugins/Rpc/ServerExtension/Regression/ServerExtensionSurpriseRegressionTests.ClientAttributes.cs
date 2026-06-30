using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed partial class ServerExtensionSurpriseRegressionTests
{
    [Fact]
    public void Standalone_ServerExtensionClient_reports_DBXK100()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;

            namespace Sample;

            public sealed class RemoteControl : IServerExtensionClientAccessor
            {
                public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; } = null!;
            }

            [ServerExtensionClient(typeof(RemoteControl))]
            [ServerExtension("standalone")]
            public sealed partial class StandaloneKernel
            {
                public int Count(HookContext ctx) => 1;
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("[ServerExtensionClient]", StringComparison.Ordinal) &&
                 d.GetMessage().Contains("service-backed", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => d.Id.StartsWith("CS", StringComparison.Ordinal));
    }
}
