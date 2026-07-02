using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginServerMemberShapeRegressionTests
{
    [Fact]
    public void Generated_plugin_server_rejects_non_public_world_interface_methods()
    {
        var diagnostics = PluginServerGenerationTestDriver.Diagnostics(ServerSource("""
                    private ValueTask<int> HiddenAsync() => default;
                    ValueTask<int> VisibleAsync();
            """));

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.Severity == DiagnosticSeverity.Error &&
                          diagnostic.GetMessage().Contains("non-public interface method 'HiddenAsync'", StringComparison.Ordinal));
    }

    [Fact]
    public void Generated_plugin_server_rejects_non_public_world_interface_properties()
    {
        var diagnostics = PluginServerGenerationTestDriver.Diagnostics(ServerSource("""
                    private int Hidden => 0;
                    int Visible { get; }
            """));

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.Severity == DiagnosticSeverity.Error &&
                          diagnostic.GetMessage().Contains("interface property 'Hidden'", StringComparison.Ordinal));
    }
}
