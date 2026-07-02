using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionUnsupportedFrameworkStructTests
{
    [Fact]
    public void Server_extension_accepts_cancellation_token_payload_parameters()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using System.Threading;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            [ServerExtension("cancellation-token-value")]
            public sealed partial class CancellationTokenValueKernel
            {
                public CancellationToken Echo(CancellationToken value, int marker, HookContext ctx) => value;
            }
            """);

        Assert.DoesNotContain(
            diagnostics,
            d => d.Id == "DBXK100" &&
                d.GetMessage().Contains("System.Threading.CancellationToken", StringComparison.Ordinal));
    }

    [Fact]
    public void Server_extension_rejects_cancellation_token_map_keys()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using System.Collections.Generic;
            using System.Threading;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            [ServerExtension("cancellation-token-map-key")]
            public sealed partial class CancellationTokenMapKeyKernel
            {
                public int Count(Dictionary<CancellationToken, int> values, HookContext ctx) => 0;
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                d.GetMessage().Contains("System.Threading.CancellationToken", StringComparison.Ordinal) &&
                d.GetMessage().Contains("map key", StringComparison.OrdinalIgnoreCase));
    }
}
