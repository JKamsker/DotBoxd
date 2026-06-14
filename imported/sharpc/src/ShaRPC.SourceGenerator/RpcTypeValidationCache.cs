using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace ShaRPC.SourceGenerator;

internal sealed class RpcTypeValidationCache
{
    private readonly object _gate = new();
    private readonly Dictionary<ITypeSymbol, bool> _subServicePayloadResults =
        new(SymbolEqualityComparer.Default);

    public bool ContainsShaRpcServiceInterface(ITypeSymbol type, CancellationToken ct) =>
        SubServicePayloadInspector.ContainsShaRpcServiceInterface(
            type,
            ct,
            this);

    public bool TryGetSubServicePayloadResult(ITypeSymbol key, out bool result)
    {
        lock (_gate)
        {
            return _subServicePayloadResults.TryGetValue(key, out result);
        }
    }

    public void SetSubServicePayloadResult(ITypeSymbol key, bool result)
    {
        lock (_gate)
        {
            _subServicePayloadResults[key] = result;
        }
    }
}
