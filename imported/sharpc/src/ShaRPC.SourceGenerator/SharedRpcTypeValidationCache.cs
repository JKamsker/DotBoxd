using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace ShaRPC.SourceGenerator;

internal static class SharedRpcTypeValidationCache
{
    private static readonly ConditionalWeakTable<Compilation, RpcTypeValidationCache> s_caches = new();

    public static RpcTypeValidationCache Get(Compilation compilation) =>
        s_caches.GetValue(compilation, static _ => new RpcTypeValidationCache());
}
