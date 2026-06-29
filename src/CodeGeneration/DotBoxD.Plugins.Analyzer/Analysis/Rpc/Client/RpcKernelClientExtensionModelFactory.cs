namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

internal static class RpcKernelClientExtensionModelFactory
{
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
            ValidateReceiver(property.ReceiverType, property.Name, "property");
        }

        if (method is not null)
        {
            ValidateReceiver(method.ReceiverType, method.Name, "method");
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
        => HasAttribute(symbol, DotBoxDMetadataNames.ServerExtensionClientAttribute) ||
           HasAttribute(symbol, DotBoxDMetadataNames.ServerExtensionMethodAttribute);

    public static bool HasReceiverExtensionAttribute(ISymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (AttributeMatches(attribute, DotBoxDMetadataNames.ServerExtensionMethodAttribute) &&
                attribute.ConstructorArguments.Length > 0 &&
                attribute.ConstructorArguments[0].Value is INamedTypeSymbol)
            {
                return true;
            }
        }

        return false;
    }

    private static RpcKernelClientPropertyExtension? ResolveClientProperty(INamedTypeSymbol kernelType)
    {
        foreach (var attribute in kernelType.GetAttributes())
        {
            if (!AttributeMatches(attribute, DotBoxDMetadataNames.ServerExtensionClientAttribute))
            {
                continue;
            }

            var receiverType = ReceiverType(attribute, "property");
            var name = OptionalName(attribute, "property") ?? DefaultPropertyName(kernelType.Name);
            ValidateMemberName(name, "property");
            return new RpcKernelClientPropertyExtension(receiverType, name);
        }

        return null;
    }

    public static RpcKernelClientMethodExtension? ResolveClientMethod(
        IMethodSymbol kernelMethod,
        INamedTypeSymbol? defaultReceiverType = null)
    {
        foreach (var attribute in kernelMethod.GetAttributes())
        {
            if (!AttributeMatches(attribute, DotBoxDMetadataNames.ServerExtensionMethodAttribute))
            {
                continue;
            }

            var receiverType = ReceiverType(attribute, "method", defaultReceiverType);
            var name = OptionalName(attribute, "method") ?? kernelMethod.Name;
            ValidateMemberName(name, "method");
            ValidateReceiver(receiverType, name, "method");
            return new RpcKernelClientMethodExtension(receiverType, name);
        }

        return null;
    }

    private static INamedTypeSymbol ReceiverType(
        AttributeData attribute,
        string memberKind,
        INamedTypeSymbol? defaultReceiverType = null)
    {
        if (attribute.ConstructorArguments.Length > 0 &&
            attribute.ConstructorArguments[0].Value is INamedTypeSymbol receiverType)
        {
            return receiverType;
        }

        if (defaultReceiverType is not null)
        {
            return defaultReceiverType;
        }

        if (attribute.ConstructorArguments.Length == 0 ||
            attribute.ConstructorArguments[0].Value is not INamedTypeSymbol)
        {
            throw new NotSupportedException($"Server extension client {memberKind} requires a receiver type.");
        }

        throw new NotSupportedException($"Server extension client {memberKind} requires a receiver type.");
    }

    private static string? OptionalName(AttributeData attribute, string memberKind)
    {
        if (attribute.ConstructorArguments.Length < 2)
        {
            return null;
        }

        if (attribute.ConstructorArguments[1].Value is null)
        {
            return null;
        }

        if (attribute.ConstructorArguments[1].Value is not string name ||
            string.IsNullOrWhiteSpace(name))
        {
            throw new NotSupportedException(
                $"Server extension client {memberKind} name must not be empty or whitespace.");
        }

        return name;
    }

    private static void ValidateMemberName(string name, string memberKind)
    {
        if (!SyntaxFacts.IsValidIdentifier(name))
        {
            throw new NotSupportedException($"Server extension client {memberKind} name '{name}' is not a valid C# identifier.");
        }
    }

    private static void ValidateReceiver(INamedTypeSymbol receiverType, string memberName, string memberKind)
    {
        EnsureAccessibleFromGeneratedClient(
            receiverType,
            $"Server extension client {memberKind} '{memberName}' receiver type '{receiverType.ToDisplayString()}'");
        RpcServerExtensionGraft.ValidateServerOwnedReceiver(
            receiverType,
            $"Server extension client {memberKind} '{memberName}' receiver type");

        if (ReceiverHasMember(receiverType, memberName))
        {
            throw new NotSupportedException(
                $"Server extension client {memberKind} '{memberName}' cannot be generated on '{receiverType.ToDisplayString()}' because that type already declares a member named '{memberName}'.");
        }

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

    private static void EnsureAccessibleFromGeneratedClient(ITypeSymbol type, string description)
        => RpcGeneratedClientAccessibility.EnsureAccessible(type, description);

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
}
