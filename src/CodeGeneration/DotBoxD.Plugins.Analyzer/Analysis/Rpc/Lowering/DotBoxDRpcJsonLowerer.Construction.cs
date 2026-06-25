using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal sealed partial class DotBoxDRpcJsonLowerer
{
    public DotBoxDRpcJsonLowerer(
        SemanticModel model,
        ICollection<string> capabilities,
        ICollection<string> effects,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, RpcInlinedBinding>? inlinedBindings = null,
        IReadOnlyCollection<string>? inlineStack = null,
        string? serverContextParameterName = null,
        ITypeSymbol? serverContextType = null)
    {
        _model = model;
        _capabilities = capabilities;
        _effects = effects;
        _cancellationToken = cancellationToken;
        _inlinedBindings = inlinedBindings;
        _inlineStack = inlineStack;
        _serverContextParameterName = serverContextParameterName;
        _serverContextType = serverContextType;
    }
}
