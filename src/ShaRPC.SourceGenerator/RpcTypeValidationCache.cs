using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace ShaRPC.SourceGenerator;

internal sealed class RpcTypeValidationCache
{
    private readonly object _gate = new();
    private readonly Dictionary<string, bool> _subServicePayloadResults =
        new(System.StringComparer.Ordinal);

    public bool ContainsShaRpcServiceInterface(ITypeSymbol type, CancellationToken ct) =>
        SubServicePayloadInspector.ContainsShaRpcServiceInterface(
            type,
            ct,
            this);

    public bool TryGetSubServicePayloadResult(string key, out bool result)
    {
        lock (_gate)
        {
            return _subServicePayloadResults.TryGetValue(key, out result);
        }
    }

    public void SetSubServicePayloadResult(string key, bool result)
    {
        lock (_gate)
        {
            _subServicePayloadResults[key] = result;
        }
    }
}
