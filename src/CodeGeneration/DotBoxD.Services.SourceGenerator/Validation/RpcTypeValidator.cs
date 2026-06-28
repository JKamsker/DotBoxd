using System.Threading;
using DotBoxD.Services.SourceGenerator.Models;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Validation;

internal static class RpcTypeValidator
{
    public static string? GetUnsupportedTypeReason(ITypeSymbol type, string role, CancellationToken ct)
        => GetUnsupportedTypeReason(type, role, ct, allowTopLevelAsyncWrapper: false);

    public static string? GetUnsupportedTypeReason(
        ITypeSymbol type,
        string role,
        CancellationToken ct,
        bool allowTopLevelAsyncWrapper)
    {
        if (ContainsTaskLikePayloadType(type, ct, allowCurrent: allowTopLevelAsyncWrapper))
        {
            return $"{role} uses Task or ValueTask as an RPC payload type; Task and ValueTask are only supported as top-level return wrappers";
        }

        if (ContainsRefLikeType(type, ct))
        {
            return $"{role} uses a ref-like type, which cannot be serialized as an RPC payload";
        }

        if (ContainsPointerType(type, ct))
        {
            return $"{role} uses a pointer type, which cannot be serialized as an RPC payload";
        }

        if (ContainsFunctionPointerType(type, ct))
        {
            return $"{role} uses a function pointer type, which cannot be serialized as an RPC payload";
        }

        return null;
    }

    public static string? GetUnsupportedSubServicePayloadReason(
        ITypeSymbol type,
        MethodReturnKind returnKind,
        string role,
        CancellationToken ct,
        RpcTypeValidationCache? cache = null)
    {
        if (returnKind is MethodReturnKind.SyncSubService or
            MethodReturnKind.TaskOfSubService or
            MethodReturnKind.ValueTaskOfSubService)
        {
            return null;
        }

        return GetUnsupportedSubServicePayloadReason(type, role, ct, cache);
    }

    public static string? GetUnsupportedSubServicePayloadReason(
        ITypeSymbol type,
        string role,
        CancellationToken ct,
        RpcTypeValidationCache? cache = null) =>
        ContainsDotBoxDServiceInterface(type, ct, cache)
            ? $"{role} contains a sub-service type; sub-services are only supported as direct TService, Task<TService>, or ValueTask<TService> return values"
            : null;

    public static bool RequiresUnsafeContext(ITypeSymbol type, CancellationToken ct) =>
        ContainsPointerType(type, ct) || ContainsFunctionPointerType(type, ct);

    private static bool ContainsDotBoxDServiceInterface(
        ITypeSymbol type,
        CancellationToken ct,
        RpcTypeValidationCache? cache) =>
        cache is null
            ? SubServicePayloadInspector.ContainsDotBoxDServiceInterface(type, ct)
            : cache.ContainsDotBoxDServiceInterface(type, ct);

    private static bool ContainsTaskLikePayloadType(
        ITypeSymbol type,
        CancellationToken ct,
        bool allowCurrent)
    {
        ct.ThrowIfCancellationRequested();

        if (type is IArrayTypeSymbol array)
        {
            return ContainsTaskLikePayloadType(array.ElementType, ct, allowCurrent: false);
        }

        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        if (IsTaskLike(named))
        {
            if (!allowCurrent)
            {
                return true;
            }

            foreach (var arg in named.TypeArguments)
            {
                ct.ThrowIfCancellationRequested();

                if (ContainsTaskLikePayloadType(arg, ct, allowCurrent: false))
                {
                    return true;
                }
            }

            return false;
        }

        foreach (var arg in named.TypeArguments)
        {
            ct.ThrowIfCancellationRequested();

            if (ContainsTaskLikePayloadType(arg, ct, allowCurrent: false))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTaskLike(INamedTypeSymbol type) =>
        (type.Name == "Task" || type.Name == "ValueTask") &&
        type.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks";

    private static bool ContainsRefLikeType(ITypeSymbol type, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (type is INamedTypeSymbol named)
        {
            if (named.IsRefLikeType)
            {
                return true;
            }

            foreach (var arg in named.TypeArguments)
            {
                ct.ThrowIfCancellationRequested();

                if (ContainsRefLikeType(arg, ct))
                {
                    return true;
                }
            }
        }

        if (type is IArrayTypeSymbol array)
        {
            return ContainsRefLikeType(array.ElementType, ct);
        }

        return false;
    }

    private static bool ContainsPointerType(ITypeSymbol type, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (type is IPointerTypeSymbol)
        {
            return true;
        }

        if (type is IArrayTypeSymbol array)
        {
            return ContainsPointerType(array.ElementType, ct);
        }

        if (type is INamedTypeSymbol named)
        {
            foreach (var arg in named.TypeArguments)
            {
                ct.ThrowIfCancellationRequested();

                if (ContainsPointerType(arg, ct))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ContainsFunctionPointerType(ITypeSymbol type, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (type is IFunctionPointerTypeSymbol)
        {
            return true;
        }

        if (type is IArrayTypeSymbol array)
        {
            return ContainsFunctionPointerType(array.ElementType, ct);
        }

        if (type is INamedTypeSymbol named)
        {
            foreach (var arg in named.TypeArguments)
            {
                ct.ThrowIfCancellationRequested();

                if (ContainsFunctionPointerType(arg, ct))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
