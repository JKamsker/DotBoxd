using DotBoxD.Plugins.Runtime;
using GeneratorNames = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Contracts;

/// <summary>
/// Pins the capability id literals the plugin analyzer bakes into generated modules to the runtime
/// constants they must equal. These are value-mirror couplings: the analyzer cannot reference the
/// runtime assemblies, so it re-declares the ids as string constants. If the runtime renames a
/// capability id, the generated module would silently request a capability the host no longer grants;
/// this test turns that drift into a red build instead.
/// </summary>
public sealed class PluginAnalyzerCapabilityContractTests
{
    [Fact]
    public void MessageWrite_capability_matches_runtime_binding()
        => Assert.Equal(PluginMessageBindings.CapabilityId, GeneratorNames.Capabilities.MessageWrite);

    [Fact]
    public void RuntimeAsync_capability_matches_runtime_id()
        => Assert.Equal(RuntimeCapabilityIds.Async, GeneratorNames.Capabilities.RuntimeAsync);
}
