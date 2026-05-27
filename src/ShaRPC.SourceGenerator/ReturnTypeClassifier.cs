using Microsoft.CodeAnalysis;

namespace ShaRPC.SourceGenerator;

internal static class ReturnTypeClassifier
{
    private const string ShaRpcServiceAttributeName = "ShaRPC.Core.Attributes.ShaRpcServiceAttribute";
    private const string SystemThreadingTasks = "System.Threading.Tasks";

    private static readonly SymbolDisplayFormat s_qualifiedFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions |
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    public static string? GetUnsupportedServiceReturnReason(ITypeSymbol returnType)
    {
        if (IsShaRpcServiceInterface(returnType))
        {
            return "synchronous sub-service returns are not supported; return Task<TService> or ValueTask<TService>";
        }

        if (!TryGetAsyncPayloadType(returnType, out var payloadType) ||
            !IsShaRpcServiceInterface(payloadType))
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
        out string? unwrappedReturnType,
        out SubServiceInfo? subService)
    {
        subService = null;

        if (returnType is INamedTypeSymbol named && named.IsGenericType)
        {
            var nsName = named.ContainingNamespace?.ToDisplayString();
            if (nsName == SystemThreadingTasks)
            {
                if (named.Name == "Task")
                {
                    var arg = named.TypeArguments[0];
                    unwrappedReturnType = arg.ToDisplayString(s_qualifiedFormat);
                    if (TryGetSubServiceInfo(arg, out var sub))
                    {
                        subService = sub;
                        return MethodReturnKind.TaskOfSubService;
                    }
                    return MethodReturnKind.TaskOf;
                }

                if (named.Name == "ValueTask")
                {
                    var arg = named.TypeArguments[0];
                    unwrappedReturnType = arg.ToDisplayString(s_qualifiedFormat);
                    if (TryGetSubServiceInfo(arg, out var sub))
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

        unwrappedReturnType = returnType.ToDisplayString(s_qualifiedFormat);
        return MethodReturnKind.Sync;
    }

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

    private static bool IsShaRpcServiceInterface(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named || named.TypeKind != TypeKind.Interface)
        {
            return false;
        }

        foreach (var attr in named.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == ShaRpcServiceAttributeName)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetSubServiceInfo(ITypeSymbol type, out SubServiceInfo info)
    {
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
            if (arg.Key == "Name" && arg.Value.Value is string customName)
            {
                serviceName = customName;
            }
        }

        info = new SubServiceInfo(
            QualifiedInterfaceName: named.ToDisplayString(s_qualifiedFormat),
            ServiceName: LiteralHelpers.EscapeStringLiteral(serviceName));
        return true;
    }
}
