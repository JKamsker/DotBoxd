using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionUnsupportedFrameworkStructTests
{
    [Fact]
    public void Server_extension_rejects_cancellation_token_payload_parameters()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using System;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            [ServerExtension("cancellation-token-value")]
            public sealed partial class CancellationTokenValueKernel
            {
                public int UseCancellationToken(System.Threading.CancellationToken value, HookContext ctx) => 0;
            }
            """);

        AssertUnsupported(diagnostics, "System.Threading.CancellationToken");
    }

    private static void AssertUnsupported(IEnumerable<Microsoft.CodeAnalysis.Diagnostic> diagnostics, string typeName)
        => Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" && d.GetMessage().Contains(typeName, StringComparison.Ordinal));
}
