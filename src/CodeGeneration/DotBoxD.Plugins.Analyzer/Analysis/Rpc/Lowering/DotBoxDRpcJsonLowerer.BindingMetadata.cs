using DotBoxD.Plugins.Analyzer.Analysis.Lowering;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal sealed partial class DotBoxDRpcJsonLowerer
{
    private void AddBindingMetadata((string BindingId, string? Capability, IReadOnlyList<string> Effects, bool IsAsync) binding)
    {
        if (binding.Capability is { Length: > 0 } capability)
        {
            _capabilities.Add(capability);
        }

        foreach (var effect in binding.Effects)
        {
            _effects.Add(effect);
        }

        if (binding.IsAsync || binding.Effects.Contains(DotBoxDGenerationNames.Effects.Concurrency))
        {
            _effects.Add(DotBoxDGenerationNames.Effects.Concurrency);
            _capabilities.Add(DotBoxDGenerationNames.Capabilities.RuntimeAsync);
        }
    }
}
