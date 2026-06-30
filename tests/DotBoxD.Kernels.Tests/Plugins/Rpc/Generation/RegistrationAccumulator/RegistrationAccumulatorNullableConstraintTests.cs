using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class RegistrationAccumulatorNullableConstraintTests
{
    [Fact]
    public void Generated_accumulator_preserves_nullable_reference_type_constraints()
    {
        var generated = string.Join("\n", PluginAnalyzerGeneratedPackageFactory.GeneratedSources("""
            #nullable enable
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;

            namespace Sample;

            [GeneratePluginRegistrationAccumulator("ServiceRegistrationAccumulator", "Replace")]
            internal sealed class RemoteServiceControl
            {
                public ValueTask<string> Replace<TService, TKernel>()
                    where TService : class?
                    where TKernel : class?, TService
                    => ValueTask.FromResult("service");
            }
            """));

        Assert.Contains("where TService : class?", generated, StringComparison.Ordinal);
        Assert.Contains("where TKernel : class?, TService", generated, StringComparison.Ordinal);
    }
}
