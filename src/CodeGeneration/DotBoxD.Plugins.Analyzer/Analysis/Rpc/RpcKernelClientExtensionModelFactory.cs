namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

internal static class RpcKernelClientExtensionModelFactory
{
    private const string RegistryType = "DotBoxD.Plugins.IKernelRpcClientRegistry";

    public static RpcKernelClientExtensions Resolve(INamedTypeSymbol kernelType, IMethodSymbol kernelMethod)
    {
        var property = ResolveClientProperty(kernelType);
        var method = ResolveClientMethod(kernelMethod);
        if (property is null && method is null)
        {
            return new RpcKernelClientExtensions(null, null);
        }

        if (property is not null)
        {
            property = property with
            {
                KernelRpcInterfaceType = ValidateReceiver(property.ReceiverType, property.Name, "property")
            };
        }

        if (method is not null)
        {
            method = method with
            {
                KernelRpcInterfaceType = ValidateReceiver(method.ReceiverType, method.Name, "method")
            };
        }

        if (property is not null &&
            method is not null &&
            SymbolEqualityComparer.Default.Equals(property.ReceiverType, method.ReceiverType) &&
            string.Equals(property.Name, method.Name, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"Kernel RPC client property and method cannot both generate '{property.Name}' on '{property.ReceiverType.ToDisplayString()}'.");
        }

        return new RpcKernelClientExtensions(property, method);
    }

    public static bool HasExtensionAttribute(ISymbol symbol)
        => HasAttribute(symbol, DotBoxDGenerationNames.Metadata.KernelRpcClientPropertyAttribute) ||
           HasAttribute(symbol, DotBoxDGenerationNames.Metadata.KernelRpcClientMethodAttribute);

    private static RpcKernelClientPropertyExtension? ResolveClientProperty(INamedTypeSymbol kernelType)
    {
        foreach (var attribute in kernelType.GetAttributes())
        {
            if (!AttributeMatches(attribute, DotBoxDGenerationNames.Metadata.KernelRpcClientPropertyAttribute))
            {
                continue;
            }

            var receiverType = ReceiverType(attribute, "property");
            var name = OptionalName(attribute) ?? DefaultPropertyName(kernelType.Name);
            ValidateMemberName(name, "property");
            return new RpcKernelClientPropertyExtension(receiverType, name, KernelRpcInterfaceType: null);
        }

        return null;
    }

    private static RpcKernelClientMethodExtension? ResolveClientMethod(IMethodSymbol kernelMethod)
    {
        foreach (var attribute in kernelMethod.GetAttributes())
        {
            if (!AttributeMatches(attribute, DotBoxDGenerationNames.Metadata.KernelRpcClientMethodAttribute))
            {
                continue;
            }

            var receiverType = ReceiverType(attribute, "method");
            var name = OptionalName(attribute) ?? kernelMethod.Name;
            ValidateMemberName(name, "method");
            return new RpcKernelClientMethodExtension(receiverType, name, KernelRpcInterfaceType: null);
        }

        return null;
    }

    private static INamedTypeSymbol ReceiverType(AttributeData attribute, string memberKind)
    {
        if (attribute.ConstructorArguments.Length == 0 ||
            attribute.ConstructorArguments[0].Value is not INamedTypeSymbol receiverType)
        {
            throw new NotSupportedException($"Kernel RPC client {memberKind} requires a receiver type.");
        }

        return receiverType;
    }

    private static string? OptionalName(AttributeData attribute)
    {
        if (attribute.ConstructorArguments.Length < 2)
        {
            return null;
        }

        return attribute.ConstructorArguments[1].Value is string name && !string.IsNullOrWhiteSpace(name)
            ? name
            : null;
    }

    private static void ValidateMemberName(string name, string memberKind)
    {
        if (!SyntaxFacts.IsValidIdentifier(name))
        {
            throw new NotSupportedException($"Kernel RPC client {memberKind} name '{name}' is not a valid C# identifier.");
        }
    }

    private static INamedTypeSymbol? ValidateReceiver(INamedTypeSymbol receiverType, string memberName, string memberKind)
    {
        if (ReceiverHasMember(receiverType, memberName))
        {
            throw new NotSupportedException(
                $"Kernel RPC client {memberKind} '{memberName}' cannot be generated on '{receiverType.ToDisplayString()}' because that type already declares a member named '{memberName}'.");
        }

        var registryAccess = ReceiverKernelRpcRegistryAccess(receiverType);
        if (registryAccess is null)
        {
            throw new NotSupportedException(
                $"Kernel RPC client {memberKind} '{memberName}' requires '{receiverType.ToDisplayString()}' to expose an instance KernelRpc property assignable to {RegistryType}.");
        }

        return registryAccess.InterfaceType;
    }

    private static bool ReceiverHasMember(INamedTypeSymbol receiverType, string memberName)
    {
        for (INamedTypeSymbol? current = receiverType; current is not null; current = current.BaseType)
        {
            if (current.GetMembers(memberName).Length > 0)
            {
                return true;
            }
        }

        foreach (var interfaceType in receiverType.AllInterfaces)
        {
            if (interfaceType.GetMembers(memberName).Length > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static KernelRpcRegistryAccess? ReceiverKernelRpcRegistryAccess(INamedTypeSymbol receiverType)
    {
        for (INamedTypeSymbol? current = receiverType; current is not null; current = current.BaseType)
        {
            if (TypeHasKernelRpcRegistry(current))
            {
                return new KernelRpcRegistryAccess(InterfaceType: null);
            }
        }

        foreach (var interfaceType in receiverType.AllInterfaces)
        {
            if (TypeHasKernelRpcRegistry(interfaceType))
            {
                return new KernelRpcRegistryAccess(interfaceType);
            }
        }

        return null;
    }

    private static bool TypeHasKernelRpcRegistry(INamedTypeSymbol receiverType)
    {
        foreach (var member in receiverType.GetMembers("KernelRpc"))
        {
            if (member is IPropertySymbol { IsStatic: false, GetMethod: { } getter } property &&
                IsAccessible(getter) &&
                IsKernelRpcClientRegistry(property.Type))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAccessible(IMethodSymbol getter)
        => getter.DeclaredAccessibility is
            Accessibility.Public or
            Accessibility.Internal or
            Accessibility.ProtectedOrInternal;

    private static bool IsKernelRpcClientRegistry(ITypeSymbol type)
    {
        if (string.Equals(type.ToDisplayString(), RegistryType, StringComparison.Ordinal))
        {
            return true;
        }

        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        foreach (var interfaceType in named.AllInterfaces)
        {
            if (string.Equals(interfaceType.ToDisplayString(), RegistryType, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAttribute(ISymbol symbol, string metadataName)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (AttributeMatches(attribute, metadataName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AttributeMatches(AttributeData attribute, string metadataName)
        => string.Equals(attribute.AttributeClass?.ToDisplayString(), metadataName, StringComparison.Ordinal);

    private static string DefaultPropertyName(string kernelName)
        => kernelName.EndsWith(DotBoxDGenerationNames.KernelSuffix, StringComparison.Ordinal)
            ? kernelName.Substring(0, kernelName.Length - DotBoxDGenerationNames.KernelSuffix.Length)
            : kernelName;

    private sealed record KernelRpcRegistryAccess(INamedTypeSymbol? InterfaceType);
}
