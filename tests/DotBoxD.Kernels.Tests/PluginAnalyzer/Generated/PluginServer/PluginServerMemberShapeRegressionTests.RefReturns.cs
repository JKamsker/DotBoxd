using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginServerMemberShapeRegressionTests
{
    [Fact]
    public void Generated_plugin_server_rejects_ref_return_forwarded_methods()
    {
        var diagnostics = PluginServerGenerationTestDriver.Diagnostics(ServerSource("""
                    ref int Current();
            """));

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.Severity == DiagnosticSeverity.Error &&
                          diagnostic.GetMessage().Contains("ref returns", StringComparison.Ordinal));
    }

    [Fact]
    public void Generated_plugin_server_rejects_ref_return_forwarded_properties()
    {
        var diagnostics = PluginServerGenerationTestDriver.Diagnostics(ServerSource("""
                    ref int Current { get; }
            """));

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.Severity == DiagnosticSeverity.Error &&
                          diagnostic.GetMessage().Contains("ref returns", StringComparison.Ordinal));
    }
}
