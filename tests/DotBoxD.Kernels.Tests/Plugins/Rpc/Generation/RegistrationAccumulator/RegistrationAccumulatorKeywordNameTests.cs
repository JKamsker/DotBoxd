using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class RegistrationAccumulatorKeywordNameTests
{
    [Fact]
    public void Target_accumulator_keyword_name_reports_dbxk100()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;

            namespace Sample;

            [GeneratePluginRegistrationAccumulator("event", "Replace")]
            internal sealed class RemoteServiceControl
            {
                public ValueTask<string> Replace<TService, TKernel>()
                    where TService : class
                    where TKernel : class, TService
                    => ValueTask.FromResult("service");
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("event", StringComparison.Ordinal) &&
                 d.GetMessage().Contains("accumulator", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(diagnostics, d => d.Id.StartsWith("CS", StringComparison.Ordinal));
    }

    [Fact]
    public void Root_accumulator_keyword_name_reports_dbxk100()
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

            [GeneratePluginRegistrationRootAccumulator("class")]
            internal sealed class RemoteWorldControl
            {
                public RemoteMonsterControl Monsters { get; } = new();
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("class", StringComparison.Ordinal) &&
                 d.GetMessage().Contains("accumulator", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(diagnostics, d => d.Id.StartsWith("CS", StringComparison.Ordinal));
    }
}
