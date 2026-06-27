using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionDerivedDtoRegressionTests
{
    [Fact]
    public void Server_extension_applies_numeric_conversion_to_derived_dto_member()
    {
        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator("""
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record WideDto(int Count)
            {
                public long Wide => Count;
            }

            [ServerExtension("wide-derived")]
            public sealed partial class WideKernel
            {
                public WideDto Read(HookContext ctx) => new WideDto(3);
            }
            """);
        var generated = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("numeric.toI64", generated, StringComparison.Ordinal);
    }
}
