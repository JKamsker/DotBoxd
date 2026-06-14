using System.Threading;
using Microsoft.CodeAnalysis;

namespace ShaRPC.SourceGenerator;

internal static class ReturnTypeClassifier
{
    private const string ShaRpcServiceAttributeName = "ShaRPC.Core.Attributes.ShaRpcServiceAttribute";
    private const string SystemCollectionsGeneric = "System.Collections.Generic";
    private const string SystemIO = "System.IO";
    private const string SystemIOPipelines = "System.IO.Pipelines";
    private const string SystemThreadingTasks = "System.Threading.Tasks";

    private static readonly SymbolDisplayFormat s_qualifiedFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions |
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private static readonly SymbolDisplayFormat s_qualifiedIdentityFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions |
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    public static string? GetUnsupportedServiceReturnReason(ITypeSymbol returnType, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (IsShaRpcServiceInterface(returnType, ct))
        {
            return "synchronous sub-service returns are not supported; return Task<TService> or ValueTask<TService>";
        }

        if (!TryGetAsyncPayloadType(returnType, out var payloadType) ||
            !IsShaRpcServiceInterface(payloadType, ct))
        {
            return null;
        }

        if (payloadType is INamedTypeSymbol named)
        {
            if (named.IsGenericType)
            {
                return "generic sub-service return types are not supported";
            }

            if (named.ContainingType is not null)
            {
                return "nested sub-service return types are not supported";
            }
        }

        return null;
    }

    public static MethodReturnKind Classify(
        ITypeSymbol returnType,
        CancellationToken ct,
        out string? unwrappedReturnType,
        out SubServiceInfo? subService)
    {
        ct.ThrowIfCancellationRequested();

        subService = null;

        if (returnType is INamedTypeSymbol named && named.IsGenericType)
        {
            var nsName = named.ContainingNamespace?.ToDisplayString();
            if (nsName == SystemThreadingTasks)
            {
                if (named.Name == "Task")
                {
                    var arg = named.TypeArguments[0];
                    if (TryGetAsyncEnumerableItemType(arg, out var itemType))
                    {
                        unwrappedReturnType = itemType.ToDisplayString(s_qualifiedFormat);
                        return MethodReturnKind.TaskOfAsyncEnumerable;
                    }

                    if (IsStream(arg))
                    {
                        unwrappedReturnType = arg.ToDisplayString(s_qualifiedFormat);
                        return MethodReturnKind.TaskOfStream;
                    }

                    if (IsPipe(arg))
                    {
                        unwrappedReturnType = arg.ToDisplayString(s_qualifiedFormat);
                        return MethodReturnKind.TaskOfPipe;
                    }

                    unwrappedReturnType = arg.ToDisplayString(s_qualifiedFormat);
                    if (TryGetSubServiceInfo(arg, ct, out var sub))
                    {
                        subService = sub;
                        return MethodReturnKind.TaskOfSubService;
                    }
                    return MethodReturnKind.TaskOf;
                }

                if (named.Name == "ValueTask")
                {
                    var arg = named.TypeArguments[0];
                    if (TryGetAsyncEnumerableItemType(arg, out var itemType))
                    {
                        unwrappedReturnType = itemType.ToDisplayString(s_qualifiedFormat);
                        return MethodReturnKind.ValueTaskOfAsyncEnumerable;
                    }

                    if (IsStream(arg))
                    {
                        unwrappedReturnType = arg.ToDisplayString(s_qualifiedFormat);
                        return MethodReturnKind.ValueTaskOfStream;
                    }

                    if (IsPipe(arg))
                    {
                        unwrappedReturnType = arg.ToDisplayString(s_qualifiedFormat);
                        return MethodReturnKind.ValueTaskOfPipe;
                    }

                    unwrappedReturnType = arg.ToDisplayString(s_qualifiedFormat);
                    if (TryGetSubServiceInfo(arg, ct, out var sub))
                    {
                        subService = sub;
                        return MethodReturnKind.ValueTaskOfSubService;
                    }
                    return MethodReturnKind.ValueTaskOf;
                }
            }
        }

        var rNs = returnType.ContainingNamespace?.ToDisplayString();
        if (rNs == SystemThreadingTasks)
        {
            if (returnType.Name == "Task")
            {
                unwrappedReturnType = null;
                return MethodReturnKind.Task;
            }

            if (returnType.Name == "ValueTask")
            {
                unwrappedReturnType = null;
                return MethodReturnKind.ValueTask;
            }
        }

        if (returnType.SpecialType == SpecialType.System_Void)
        {
            unwrappedReturnType = null;
            return MethodReturnKind.Void;
        }

        if (TryGetAsyncEnumerableItemType(returnType, out var enumerableItemType))
        {
            unwrappedReturnType = enumerableItemType.ToDisplayString(s_qualifiedFormat);
            return MethodReturnKind.AsyncEnumerable;
        }

        if (IsStream(returnType))
        {
            unwrappedReturnType = returnType.ToDisplayString(s_qualifiedFormat);
            return MethodReturnKind.Stream;
        }

        if (IsPipe(returnType))
        {
            unwrappedReturnType = returnType.ToDisplayString(s_qualifiedFormat);
            return MethodReturnKind.Pipe;
        }

        unwrappedReturnType = returnType.ToDisplayString(s_qualifiedFormat);
        return MethodReturnKind.Sync;
    }

    public static bool TryGetAsyncEnumerableItemType(ITypeSymbol type, out ITypeSymbol itemType)
    {
        itemType = null!;
        if (type is not INamedTypeSymbol { IsGenericType: true } named)
        {
            return false;
        }

        if (named.Name != "IAsyncEnumerable" ||
            named.ContainingNamespace?.ToDisplayString() != SystemCollectionsGeneric)
        {
            return false;
        }

        itemType = named.TypeArguments[0];
        return true;
    }

    public static bool IsStream(ITypeSymbol type) =>
        type.Name == "Stream" &&
        type.ContainingNamespace?.ToDisplayString() == SystemIO;

    public static bool IsPipe(ITypeSymbol type) =>
        type.Name == "Pipe" &&
        type.ContainingNamespace?.ToDisplayString() == SystemIOPipelines;

    private static bool TryGetAsyncPayloadType(ITypeSymbol type, out ITypeSymbol payloadType)
    {
        payloadType = null!;
        if (type is not INamedTypeSymbol named || !named.IsGenericType)
        {
            return false;
        }

        var nsName = named.ContainingNamespace?.ToDisplayString();
        if (nsName != SystemThreadingTasks ||
            (named.Name != "Task" && named.Name != "ValueTask"))
        {
            return false;
        }

        payloadType = named.TypeArguments[0];
        return true;
    }

    private static bool IsShaRpcServiceInterface(ITypeSymbol type, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (type is not INamedTypeSymbol named || named.TypeKind != TypeKind.Interface)
        {
            return false;
        }

        foreach (var attr in named.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();

            if (attr.AttributeClass?.ToDisplayString() == ShaRpcServiceAttributeName)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetSubServiceInfo(ITypeSymbol type, CancellationToken ct, out SubServiceInfo info)
    {
        ct.ThrowIfCancellationRequested();

        info = null!;
        if (type is not INamedTypeSymbol named || named.TypeKind != TypeKind.Interface)
        {
            return false;
        }

        if (named.IsGenericType || named.ContainingType is not null)
        {
            return false;
        }

        AttributeData? serviceAttr = null;
        foreach (var attr in named.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();

            if (attr.AttributeClass?.ToDisplayString() == ShaRpcServiceAttributeName)
            {
                serviceAttr = attr;
                break;
            }
        }
        if (serviceAttr is null) return false;

        string serviceName = named.Name;
        foreach (var arg in serviceAttr.NamedArguments)
        {
            ct.ThrowIfCancellationRequested();

            if (arg.Key == "Name" && arg.Value.Value is string customName)
            {
                serviceName = customName;
            }
        }

        info = new SubServiceInfo(
            QualifiedInterfaceName: named.ToDisplayString(s_qualifiedIdentityFormat),
            ServiceName: LiteralHelpers.EscapeStringLiteral(serviceName),
            AllowsNull: named.NullableAnnotation == NullableAnnotation.Annotated);
        return true;
    }
}
