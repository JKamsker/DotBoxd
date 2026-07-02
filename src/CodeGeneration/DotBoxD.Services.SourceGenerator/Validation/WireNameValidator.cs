using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using DotBoxD.Services.SourceGenerator.Models;
using DotBoxD.Shared.Routing;

namespace DotBoxD.Services.SourceGenerator.Validation;

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
                $"wire method name '{method.RawRpcName}' is used by multiple service methods; give each overload a distinct [DotBoxDMethod(Name = ...)] value";
            methods[i] = method with { UnsupportedReason = reason };
            methodDiagnostics.Add(new MethodDiagnostic(interfaceName, method.Name, reason, methodLocations[i]));
        }
    }
}

internal static class RouteNameBudgetValidator
{
    public static string? GetUnsupportedServiceNameReason(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "[DotBoxDService(Name = ...)] wire name must not be empty or whitespace";
        }

        return GetOverBudgetReason(name, "service", "ServiceName");
    }

    public static string? GetUnsupportedMethodNameReason(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "[DotBoxDMethod(Name = ...)] wire name must not be empty or whitespace";
        }

        return GetOverBudgetReason(name, "method", "MethodName");
    }

    private static string? GetOverBudgetReason(string name, string routeKind, string requestField)
    {
        var byteCount = Encoding.UTF8.GetByteCount(name);
        if (byteCount <= RpcRequestRouteNameLimits.MaxUtf8Bytes)
        {
            return null;
        }

        return $"wire {routeKind} routing key is {byteCount} UTF-8 bytes; the RPC request {requestField} limit is {RpcRequestRouteNameLimits.MaxUtf8Bytes} bytes";
    }
}
