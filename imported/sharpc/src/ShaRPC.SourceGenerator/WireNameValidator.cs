using System;
using System.Collections.Generic;
using System.Threading;

namespace ShaRPC.SourceGenerator;

internal static class WireNameValidator
{
    public static void MarkDuplicateWireNames(
        string interfaceName,
        List<MethodModel> methods,
        List<DiagnosticLocation> methodLocations,
        List<MethodDiagnostic> methodDiagnostics,
        CancellationToken ct)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var method in methods)
        {
            ct.ThrowIfCancellationRequested();

            if (method.UnsupportedReason is not null)
            {
                continue;
            }

            counts.TryGetValue(method.RpcName, out var count);
            counts[method.RpcName] = count + 1;
        }

        for (var i = 0; i < methods.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var method = methods[i];
            if (method.UnsupportedReason is not null ||
                !counts.TryGetValue(method.RpcName, out var count) ||
                count < 2)
            {
                continue;
            }

            var reason =
                $"wire method name '{method.RawRpcName}' is used by multiple service methods; give each overload a distinct [ShaRpcMethod(Name = ...)] value";
            methods[i] = method with { UnsupportedReason = reason };
            methodDiagnostics.Add(new MethodDiagnostic(interfaceName, method.Name, reason, methodLocations[i]));
        }
    }
}
