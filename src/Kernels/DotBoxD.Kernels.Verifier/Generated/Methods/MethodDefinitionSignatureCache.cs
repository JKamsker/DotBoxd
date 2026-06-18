using System.Reflection.Metadata;

namespace DotBoxD.Kernels.Verifier.Generated.Methods;

internal sealed class MethodDefinitionSignatureCache
{
    private readonly Dictionary<MethodDefinitionHandle, MethodSignature<string>> _signatures = [];

    public MethodSignature<string> Get(MethodDefinitionHandle handle, MethodDefinition method)
    {
        if (_signatures.TryGetValue(handle, out var signature))
        {
            return signature;
        }

        signature = method.DecodeSignature(MethodSignatureNameProvider.Instance, genericContext: null);
        _signatures.Add(handle, signature);
        return signature;
    }
}
