using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal static class InvokeAsyncGeneratedServerInterfaceResolver
{
    public static bool TryResolve(
        Compilation compilation,
        INamedTypeSymbol type,
        CancellationToken cancellationToken,
        out string receiverType,
        out string? serverAccessType,
        out INamedTypeSymbol worldType)
    {
        if (type.TypeKind != TypeKind.Error && !HasGeneratedServerShape(type))
        {
            receiverType = string.Empty;
            serverAccessType = null;
            worldType = null!;
            return false;
        }

        if (TryResolveByConvention(compilation, type, out receiverType, out serverAccessType, out worldType))
        {
            return true;
        }

        return TryResolveByFacadeFallback(
            compilation,
            type,
            cancellationToken,
            out receiverType,
            out serverAccessType,
            out worldType);
    }

    private static bool TryResolveByConvention(
        Compilation compilation,
        INamedTypeSymbol type,
        out string receiverType,
        out string? serverAccessType,
        out INamedTypeSymbol worldType)
    {
        foreach (var metadataName in CandidateWorldMetadataNames(type))
        {
            if (compilation.GetTypeByMetadataName(metadataName) is { } candidate &&
                HasDotBoxDServiceAttribute(candidate) &&
                string.Equals(type.Name, ServerInterfaceName(candidate), StringComparison.Ordinal))
            {
                worldType = candidate;
                receiverType = PluginServerInterfaceTypeName(worldType);
                serverAccessType = TypeName(type);
                return true;
            }
        }

        receiverType = string.Empty;
        serverAccessType = null;
        worldType = null!;
        return false;
    }

    private static IEnumerable<string> CandidateWorldMetadataNames(INamedTypeSymbol type)
    {
        var ns = type.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : type.ContainingNamespace.ToDisplayString() + ".";
        var stem = type.Name.Substring(1, type.Name.Length - 1 - "Server".Length);
        yield return ns + "I" + stem + "Access";
        yield return ns + "I" + stem;
    }

    private static bool TryResolveByFacadeFallback(
        Compilation compilation,
        INamedTypeSymbol type,
        CancellationToken cancellationToken,
        out string receiverType,
        out string? serverAccessType,
        out INamedTypeSymbol worldType)
    {
        receiverType = string.Empty;
        serverAccessType = null;
        worldType = null!;
        var candidates = new List<(INamedTypeSymbol Facade, INamedTypeSymbol World)>();
        var expectedNamespace = type.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : type.ContainingNamespace.ToDisplayString();
        foreach (var symbol in compilation.GetSymbolsWithName(
                     static name => name.EndsWith("Server", StringComparison.Ordinal),
                     SymbolFilter.Type,
                     cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (symbol is not INamedTypeSymbol candidate ||
                !TryResolveWorld(candidate, out var candidateWorld) ||
                !string.Equals(type.Name, ServerInterfaceName(candidateWorld), StringComparison.Ordinal))
            {
                continue;
            }

            var candidateNamespace = candidate.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : candidate.ContainingNamespace.ToDisplayString();
            if (!string.IsNullOrEmpty(expectedNamespace) &&
                string.Equals(expectedNamespace, candidateNamespace, StringComparison.Ordinal))
            {
                receiverType = PluginServerInterfaceTypeName(candidateWorld);
                serverAccessType = ServerInterfaceTypeName(candidate, candidateWorld);
                worldType = candidateWorld;
                return true;
            }

            candidates.Add((candidate, candidateWorld));
        }

        if (candidates.Count != 1)
        {
            return false;
        }

        receiverType = PluginServerInterfaceTypeName(candidates[0].World);
        serverAccessType = ServerInterfaceTypeName(candidates[0].Facade, candidates[0].World);
        worldType = candidates[0].World;
        return true;
    }

    private static bool TryResolveWorld(INamedTypeSymbol type, out INamedTypeSymbol worldType)
    {
        worldType = null!;
        if (!HasGeneratePluginServerAttribute(type))
        {
            return false;
        }

        var found = false;
        foreach (var candidate in type.Interfaces)
        {
            if (HasDotBoxDServiceAttribute(candidate))
            {
                if (found)
                {
                    worldType = null!;
                    return false;
                }

                worldType = candidate;
                found = true;
            }
        }

        return found;
    }

    private static bool HasGeneratePluginServerAttribute(INamedTypeSymbol type)
        => HasAttribute(type, DotBoxDMetadataNames.GeneratePluginServerAttribute);

    private static bool HasDotBoxDServiceAttribute(INamedTypeSymbol type)
        => HasAttribute(type, DotBoxDMetadataNames.RpcServiceAttribute);

    private static bool HasGeneratedServerShape(INamedTypeSymbol type)
        => type.GetMembers("Services").OfType<IPropertySymbol>().Any(property =>
               SymbolEqualityComparer.Default.Equals(property.Type, type)) &&
           type.GetMembers("WireClient").OfType<IPropertySymbol>().Any() &&
           type.GetMembers("EnsureAnonymousKernelAsync").OfType<IMethodSymbol>().Any();

    private static bool HasAttribute(INamedTypeSymbol type, string metadataName)
    {
        foreach (var attribute in type.GetAttributes())
        {
            var attributeName = attribute.AttributeClass?.ToDisplayString();
            var matches = string.Equals(metadataName, DotBoxDMetadataNames.RpcServiceAttribute, StringComparison.Ordinal)
                ? DotBoxDMetadataNames.IsRpcServiceAttribute(attributeName)
                : string.Equals(attributeName, metadataName, StringComparison.Ordinal);
            if (matches)
            {
                return true;
            }
        }

        return false;
    }

    private static string ServerInterfaceTypeName(INamedTypeSymbol facadeType, INamedTypeSymbol worldType)
    {
        var ns = facadeType.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : facadeType.ContainingNamespace.ToDisplayString() + ".";
        return "global::" + ns + ServerInterfaceName(worldType);
    }

    private static string PluginServerInterfaceTypeName(INamedTypeSymbol worldType)
        => "global::DotBoxD.Abstractions.IPluginServer<" + TypeName(worldType) + ">";

    private static string ServerInterfaceName(INamedTypeSymbol worldType)
    {
        var name = worldType.Name;
        if (name.StartsWith("I", StringComparison.Ordinal) && name.Length > 1 && char.IsUpper(name[1]))
        {
            name = name.Substring(1);
        }

        if (name.EndsWith("Access", StringComparison.Ordinal) && name.Length > "Access".Length)
        {
            name = name.Substring(0, name.Length - "Access".Length);
        }

        return "I" + name + "Server";
    }

    private static string TypeName(INamedTypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
}
