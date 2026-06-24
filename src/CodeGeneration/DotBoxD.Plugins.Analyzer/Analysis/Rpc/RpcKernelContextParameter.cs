using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class RpcKernelContextParameter
{
    private const string GeneratedPluginServerRegistryAttribute =
        "DotBoxD.Abstractions.GeneratedPluginServerRegistryAttribute";

    public static bool IsSupported(IParameterSymbol parameter, Compilation compilation)
        => parameter.RefKind == RefKind.None &&
           (IsRawHookContext(parameter.Type) ||
            IsGeneratedContext(parameter.Type, compilation));

    private static bool IsRawHookContext(ITypeSymbol type)
        => string.Equals(type.ToDisplayString(), DotBoxDMetadataNames.HookContextType, StringComparison.Ordinal);

    private static bool IsGeneratedContext(ITypeSymbol type, Compilation compilation)
    {
        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Class } named)
        {
            return false;
        }

        return SymbolEqualityComparer.Default.Equals(named.ContainingAssembly, compilation.Assembly)
            ? IsSameCompilationGeneratedContext(named, compilation)
            : IsMarkedGeneratedContext(named);
    }

    private static bool IsMarkedGeneratedContext(INamedTypeSymbol contextType)
    {
        if (!ReferencesGeneratedRegistryContract(contextType.ContainingAssembly))
        {
            return false;
        }

        foreach (var registryType in TypesInNamespace(contextType.ContainingAssembly.GlobalNamespace))
        {
            if (registryType.DeclaringSyntaxReferences.Length == 0 &&
                IsGeneratedRegistryForContext(registryType, contextType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ReferencesGeneratedRegistryContract(IAssemblySymbol assembly)
    {
        foreach (var module in assembly.Modules)
        {
            foreach (var referenced in module.ReferencedAssemblySymbols)
            {
                if (string.Equals(referenced.Identity.Name, "DotBoxD.Abstractions", StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return string.Equals(assembly.Identity.Name, "DotBoxD.Abstractions", StringComparison.Ordinal);
    }

    private static IEnumerable<INamedTypeSymbol> TypesInNamespace(INamespaceSymbol ns)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            yield return type;
            foreach (var nested in NestedTypes(type))
            {
                yield return nested;
            }
        }

        foreach (var child in ns.GetNamespaceMembers())
        {
            foreach (var type in TypesInNamespace(child))
            {
                yield return type;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> NestedTypes(INamedTypeSymbol type)
    {
        foreach (var nested in type.GetTypeMembers())
        {
            yield return nested;
            foreach (var descendant in NestedTypes(nested))
            {
                yield return descendant;
            }
        }
    }

    private static bool IsGeneratedRegistryForContext(INamedTypeSymbol registryType, INamedTypeSymbol contextType)
    {
        foreach (var attribute in registryType.GetAttributes())
        {
            if (string.Equals(
                    attribute.AttributeClass?.ToDisplayString(),
                    GeneratedPluginServerRegistryAttribute,
                    StringComparison.Ordinal) &&
                attribute.ConstructorArguments.Length == 3 &&
                attribute.ConstructorArguments[2].Value is INamedTypeSymbol markedContext &&
                SymbolEqualityComparer.Default.Equals(markedContext, contextType) &&
                SymbolEqualityComparer.Default.Equals(registryType.ContainingAssembly, contextType.ContainingAssembly))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSameCompilationGeneratedContext(INamedTypeSymbol type, Compilation compilation)
    {
        foreach (var symbol in compilation.GetSymbolsWithName(static _ => true, SymbolFilter.Type))
        {
            if (symbol is INamedTypeSymbol server &&
                GeneratedContextType(server) is { } generatedContext &&
                SymbolEqualityComparer.Default.Equals(generatedContext, type))
            {
                return true;
            }
        }

        return false;
    }

    private static INamedTypeSymbol? GeneratedContextType(INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (!string.Equals(
                    attribute.AttributeClass?.ToDisplayString(),
                    DotBoxDMetadataNames.GeneratePluginServerAttribute,
                    StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var argument in attribute.NamedArguments)
            {
                if (string.Equals(argument.Key, "Context", StringComparison.Ordinal) &&
                    argument.Value.Value is INamedTypeSymbol contextType)
                {
                    return contextType;
                }
            }
        }

        return null;
    }
}
