using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class RegistrationAccumulatorChildAccessibilityTests
{
    [Fact]
    public void Root_child_property_with_private_getter_reports_diagnostic()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;

            namespace Sample;

            [GeneratePluginRegistrationAccumulator("RemoteMonsterExtensionAccumulator", "Extend")]
            internal sealed class RemoteMonsterControl
            {
                public ValueTask<string> Extend<TService, TKernel>()
                    where TService : class
                    where TKernel : class
                    => ValueTask.FromResult("extension");
            }

            [GeneratePluginRegistrationRootAccumulator("WorldRegistrationAccumulator")]
            internal sealed class RemoteWorldControl
            {
                public RemoteMonsterControl Monsters { private get; set; } = new();
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("Monsters", StringComparison.Ordinal) &&
                 d.GetMessage().Contains("getter", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(diagnostics, d => d.Id.StartsWith("CS", StringComparison.Ordinal));
    }
}
