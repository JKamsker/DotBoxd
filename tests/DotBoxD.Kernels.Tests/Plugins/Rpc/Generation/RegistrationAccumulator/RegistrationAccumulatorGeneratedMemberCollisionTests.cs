using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class RegistrationAccumulatorGeneratedMemberCollisionTests
{
    [Fact]
    public void Generated_root_accumulator_rejects_child_properties_with_colliding_field_names()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;

            namespace Sample;

            [GeneratePluginRegistrationRootAccumulator("WorldRegistrationAccumulator")]
            internal sealed class RemoteWorldControl
            {
                public RemoteMonsterControl Monsters { get; } = new();

                public RemoteMonsterControl monsters { get; } = new();
            }

            [GeneratePluginRegistrationAccumulator("RemoteMonsterExtensionAccumulator", "Extend")]
            internal sealed class RemoteMonsterControl
            {
                public ValueTask<string> Extend<TService, TKernel>()
                    where TService : class
                    where TKernel : class
                    => ValueTask.FromResult("extension");
            }
            """);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("Monsters", StringComparison.Ordinal) &&
                          diagnostic.GetMessage().Contains("monsters", StringComparison.Ordinal) &&
                          diagnostic.GetMessage().Contains("collid", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id is "CS0102" or "CS0229");
    }

    [Fact]
    public void Root_child_named_like_flush_helper_reports_dbxk100()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;

            namespace Sample;

            [GeneratePluginRegistrationRootAccumulator("WorldRegistrationAccumulator")]
            internal sealed class RemoteWorldControl
            {
                public RemoteMonsterControl FlushAsync { get; } = new();
            }

            [GeneratePluginRegistrationAccumulator("RemoteMonsterExtensionAccumulator", "Extend")]
            internal sealed class RemoteMonsterControl
            {
                public ValueTask<string> Extend<TService, TKernel>()
                    where TService : class
                    where TKernel : class
                    => ValueTask.FromResult("extension");
            }
            """);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("FlushAsync", StringComparison.Ordinal) &&
                          diagnostic.GetMessage().Contains("collides", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id is "CS0102" or "CS0111");
    }

    [Fact]
    public void Registration_method_named_like_flush_helper_reports_dbxk100()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;

            namespace Sample;

            [GeneratePluginRegistrationAccumulator("ServiceRegistrationAccumulator", "FlushAsync")]
            internal sealed class RemoteServiceControl
            {
                public ValueTask<string> FlushAsync()
                    => ValueTask.FromResult("service");
            }
            """);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("FlushAsync", StringComparison.Ordinal) &&
                          diagnostic.GetMessage().Contains("collides", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id is "CS0102" or "CS0111");
    }
}
