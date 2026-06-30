using System.Threading;
using DotBoxD.Services.SourceGenerator.Models;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Validation;

internal static partial class RpcTypeValidator
{
    public static string? GetUnsupportedTypeReason(ITypeSymbol type, string role, CancellationToken ct)
        => GetUnsupportedTypeReason(type, role, ct, allowTopLevelAsyncWrapper: false);

    public static string? GetUnsupportedTypeReason(
        ITypeSymbol type,
        string role,
        CancellationToken ct,
        bool allowTopLevelAsyncWrapper,
        bool allowCurrentTransportShape = false,
        bool allowCurrentCancellationToken = false,
        ITypeSymbol? cancellationTokenSymbol = null) =>
        GetUnsupportedTypeReasonCore(
            type,
            role,
            ct,
            allowTopLevelAsyncWrapper,
            allowCurrentTransportShape,
            allowCurrentCancellationToken,
            cancellationTokenSymbol,
            inspectDtoMembers: true);

    internal static string? GetUnsupportedDirectTypeReason(
        ITypeSymbol type,
        string role,
        CancellationToken ct,
        bool allowTopLevelAsyncWrapper,
        bool allowCurrentTransportShape = false,
        bool allowCurrentCancellationToken = false,
        ITypeSymbol? cancellationTokenSymbol = null) =>
        GetUnsupportedTypeReasonCore(
            type,
            role,
            ct,
            allowTopLevelAsyncWrapper,
            allowCurrentTransportShape,
            allowCurrentCancellationToken,
            cancellationTokenSymbol,
            inspectDtoMembers: false);

    private static string? GetUnsupportedTypeReasonCore(
        ITypeSymbol type,
        string role,
        CancellationToken ct,
        bool allowTopLevelAsyncWrapper,
        bool allowCurrentTransportShape = false,
        bool allowCurrentCancellationToken = false,
        ITypeSymbol? cancellationTokenSymbol = null,
        bool inspectDtoMembers = true)
    {
        if (ContainsTaskLikePayloadType(type, ct, allowCurrent: allowTopLevelAsyncWrapper))
        {
            return $"{role} uses Task or ValueTask as an RPC payload type; Task and ValueTask are only supported as top-level return wrappers";
        }

        if (StreamingShapeTypeValidator.ContainsConcreteStreamingShape(type, ct, out var streamingReplacement))
        {
            return $"{role} uses a concrete streaming-compatible type; use {streamingReplacement} directly in RPC signatures";
        }

        if (ContainsStreamingOrControlPayloadType(
                type,
                ct,
                allowCurrentTransportShape,
                allowCurrentCancellationToken,
                allowTopLevelAsyncWrapper,
                cancellationTokenSymbol))
        {
            return $"{role} uses a streaming or control type as an RPC payload; Stream, Pipe, IAsyncEnumerable<T>, RpcStreamHandle, and CancellationToken are only supported as direct streaming/control RPC shapes";
        }

        if (ContainsOpenEndedPayloadType(type, ct))
        {
            return $"{role} uses object or dynamic as an RPC payload type; declare a concrete payload type so the wire contract can be reconstructed";
        }

        if (ContainsDelegatePayloadType(type, ct))
        {
            return $"{role} uses a delegate type as an RPC payload; delegates are executable in-process callbacks and cannot be reconstructed across RPC peers";
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

        var reconstructibilityReason = RpcPayloadReconstructibilityInspector.GetUnsupportedReason(type, role, ct);
        if (reconstructibilityReason is not null)
        {
            return reconstructibilityReason;
        }

        return inspectDtoMembers
            ? RpcPayloadMemberInspector.GetUnsupportedPayloadMemberReason(
                type,
                role,
                ct,
                allowTopLevelAsyncWrapper,
                allowCurrentTransportShape,
                allowCurrentCancellationToken,
                cancellationTokenSymbol)
            : null;
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

    private static bool ContainsOpenEndedPayloadType(ITypeSymbol type, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (type.TypeKind == TypeKind.Dynamic || type.SpecialType == SpecialType.System_Object)
        {
            return true;
        }

        if (type is IArrayTypeSymbol array)
        {
            return ContainsOpenEndedPayloadType(array.ElementType, ct);
        }

        if (type is INamedTypeSymbol named)
        {
            foreach (var arg in named.TypeArguments)
            {
                ct.ThrowIfCancellationRequested();

                if (ContainsOpenEndedPayloadType(arg, ct))
                {
                    return true;
                }
            }
        }

        return false;
    }

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

}
