namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

internal static class RpcKernelClientExtensionModelFactory
{
    private const string RegistryType = "DotBoxD.Abstractions.IServerExtensionClientRegistry";

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
                ServerExtensionsInterfaceType = ValidateReceiver(property.ReceiverType, property.Name, "property")
            };
        }

        if (method is not null)
        {
            method = method with
            {
                ServerExtensionsInterfaceType = ValidateReceiver(method.ReceiverType, method.Name, "method")
            };
        }

        if (property is not null &&
            method is not null &&
            SymbolEqualityComparer.Default.Equals(property.ReceiverType, method.ReceiverType) &&
            string.Equals(property.Name, method.Name, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"Server extension client property and method cannot both generate '{property.Name}' on '{property.ReceiverType.ToDisplayString()}'.");
        }

        return new RpcKernelClientExtensions(property, method);
    }

    public static bool HasExtensionAttribute(ISymbol symbol)
        => HasAttribute(symbol, DotBoxDGenerationNames.Metadata.ServerExtensionClientAttribute) ||
           HasAttribute(symbol, DotBoxDGenerationNames.Metadata.ServerExtensionMethodAttribute);

    private static RpcKernelClientPropertyExtension? ResolveClientProperty(INamedTypeSymbol kernelType)
    {
        foreach (var attribute in kernelType.GetAttributes())
        {
            if (!AttributeMatches(attribute, DotBoxDGenerationNames.Metadata.ServerExtensionClientAttribute))
            {
                continue;
            }

            var receiverType = ReceiverType(attribute, "property");
            var name = OptionalName(attribute) ?? DefaultPropertyName(kernelType.Name);
            ValidateMemberName(name, "property");
            return new RpcKernelClientPropertyExtension(receiverType, name, ServerExtensionsInterfaceType: null);
        }

        return null;
    }

    private static RpcKernelClientMethodExtension? ResolveClientMethod(IMethodSymbol kernelMethod)
    {
        foreach (var attribute in kernelMethod.GetAttributes())
        {
            if (!AttributeMatches(attribute, DotBoxDGenerationNames.Metadata.ServerExtensionMethodAttribute))
            {
                continue;
            }

            var receiverType = ReceiverType(attribute, "method");
            var name = OptionalName(attribute) ?? kernelMethod.Name;
            ValidateMemberName(name, "method");
            return new RpcKernelClientMethodExtension(receiverType, name, ServerExtensionsInterfaceType: null);
        }

        return null;
    }

    private static INamedTypeSymbol ReceiverType(AttributeData attribute, string memberKind)
    {
        if (attribute.ConstructorArguments.Length == 0 ||
            attribute.ConstructorArguments[0].Value is not INamedTypeSymbol receiverType)
        {
            throw new NotSupportedException($"Server extension client {memberKind} requires a receiver type.");
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
            throw new NotSupportedException($"Server extension client {memberKind} name '{name}' is not a valid C# identifier.");
        }
    }

    private static INamedTypeSymbol? ValidateReceiver(INamedTypeSymbol receiverType, string memberName, string memberKind)
    {
        EnsureAccessibleFromGeneratedClient(
            receiverType,
            $"Server extension client {memberKind} '{memberName}' receiver type '{receiverType.ToDisplayString()}'");

        if (ReceiverHasMember(receiverType, memberName))
        {
            throw new NotSupportedException(
                $"Server extension client {memberKind} '{memberName}' cannot be generated on '{receiverType.ToDisplayString()}' because that type already declares a member named '{memberName}'.");
        }

        var registryAccess = ReceiverServerExtensionRegistryAccess(receiverType);
        if (registryAccess is null)
        {
            throw new NotSupportedException(
                $"Server extension client {memberKind} '{memberName}' requires '{receiverType.ToDisplayString()}' to expose an instance ServerExtensions property assignable to {RegistryType}.");
        }

        if (registryAccess.InterfaceType is { } interfaceType)
        {
            EnsureAccessibleFromGeneratedClient(
                interfaceType,
                $"Server extension client {memberKind} '{memberName}' ServerExtensions property interface '{interfaceType.ToDisplayString()}'");
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

    private static ServerExtensionRegistryAccess? ReceiverServerExtensionRegistryAccess(INamedTypeSymbol receiverType)
    {
        for (INamedTypeSymbol? current = receiverType; current is not null; current = current.BaseType)
        {
            if (TypeHasServerExtensionRegistry(current))
            {
                return new ServerExtensionRegistryAccess(InterfaceType: null);
            }
        }

        foreach (var interfaceType in receiverType.AllInterfaces)
        {
            if (TypeHasServerExtensionRegistry(interfaceType))
            {
                return new ServerExtensionRegistryAccess(interfaceType);
            }
        }

        return null;
    }

    private static bool TypeHasServerExtensionRegistry(INamedTypeSymbol receiverType)
    {
        foreach (var member in receiverType.GetMembers("ServerExtensions"))
        {
            if (member is IPropertySymbol { IsStatic: false, GetMethod: { } getter } property &&
                IsAccessible(getter) &&
                IsServerExtensionClientRegistry(property.Type))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAccessible(IMethodSymbol getter)
        => IsAccessibleFromGeneratedClient(getter.DeclaredAccessibility);

    private static void EnsureAccessibleFromGeneratedClient(ITypeSymbol type, string description)
    {
        if (!IsTypeAccessibleFromGeneratedClient(type))
        {
            throw new NotSupportedException($"{description} must be accessible from generated client code.");
        }
    }

    private static bool IsTypeAccessibleFromGeneratedClient(ITypeSymbol type)
        => type switch
        {
            INamedTypeSymbol named => IsNamedTypeAccessibleFromGeneratedClient(named),
            IArrayTypeSymbol array => IsTypeAccessibleFromGeneratedClient(array.ElementType),
            IPointerTypeSymbol pointer => IsTypeAccessibleFromGeneratedClient(pointer.PointedAtType),
            ITypeParameterSymbol or IDynamicTypeSymbol => true,
            _ => false
        };

    private static bool IsNamedTypeAccessibleFromGeneratedClient(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            if (!IsAccessibleFromGeneratedClient(current.DeclaredAccessibility))
            {
                return false;
            }

            foreach (var typeArgument in current.TypeArguments)
            {
                if (!IsTypeAccessibleFromGeneratedClient(typeArgument))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsAccessibleFromGeneratedClient(Accessibility accessibility)
        => accessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal;

    private static bool IsServerExtensionClientRegistry(ITypeSymbol type)
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

    private sealed record ServerExtensionRegistryAccess(INamedTypeSymbol? InterfaceType);
}
