using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static partial class PluginServerFacadeModelFactory
{
    private sealed record DeclaredContext(
        INamedTypeSymbol Type,
        string Namespace,
        string? FactoryMethodName);

    private static DeclaredContext ResolveContext(
        INamedTypeSymbol serverType,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var attribute = GeneratePluginServerAttribute(serverType)
            ?? throw new NotSupportedException(
                $"Generated plugin server '{serverType.Name}' must carry [GeneratePluginServer].");
        var contextType = ContextType(attribute)
            ?? throw new NotSupportedException(
                $"Generated plugin server '{serverType.Name}' must declare Context = typeof(TContext).");

        ValidateContextShape(serverType, contextType, cancellationToken);
        ValidateContextMembers(contextType, cancellationToken);
        EnsureSingleServerOwnsContext(serverType, contextType, compilation, cancellationToken);

        var factoryName = ContextFactoryName(attribute);
        if (factoryName is not null)
        {
            ValidateContextFactory(contextType, factoryName);
        }

        var ns = contextType.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : contextType.ContainingNamespace.ToDisplayString();
        return new DeclaredContext(contextType, ns, factoryName);
    }

    private static AttributeData? GeneratePluginServerAttribute(INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (string.Equals(
                    attribute.AttributeClass?.ToDisplayString(),
                    DotBoxDMetadataNames.GeneratePluginServerAttribute,
                    StringComparison.Ordinal))
            {
                return attribute;
            }
        }

        return null;
    }

    private static INamedTypeSymbol? ContextType(AttributeData attribute)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (string.Equals(argument.Key, "Context", StringComparison.Ordinal) &&
                argument.Value.Value is INamedTypeSymbol contextType)
            {
                return contextType;
            }
        }

        return null;
    }

    private static string? ContextFactoryName(AttributeData attribute)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (!string.Equals(argument.Key, "ContextFactory", StringComparison.Ordinal))
            {
                continue;
            }

            if (argument.Value.Value is string { Length: > 0 } factoryName)
            {
                return factoryName;
            }

            throw new NotSupportedException("ContextFactory must name a static context factory method.");
        }

        return null;
    }

    private static void ValidateContextShape(
        INamedTypeSymbol serverType,
        INamedTypeSymbol contextType,
        CancellationToken cancellationToken)
    {
        if (!SymbolEqualityComparer.Default.Equals(serverType.ContainingAssembly, contextType.ContainingAssembly))
        {
            throw new NotSupportedException(
                $"Generated plugin server '{serverType.Name}' context '{contextType.ToDisplayString()}' must be declared in the same compilation.");
        }

        if (contextType.TypeKind != TypeKind.Class || contextType.IsGenericType || contextType.ContainingType is not null)
        {
            throw new NotSupportedException(
                $"Generated plugin server context '{contextType.ToDisplayString()}' must be a non-generic, non-nested partial class.");
        }

        if (IsFileLocalClass(contextType, cancellationToken))
        {
            throw new NotSupportedException(
                $"Generated plugin server context '{contextType.ToDisplayString()}' must not be file-local because generated source cannot augment a file-local type from another file.");
        }

        if (!IsPartialClass(contextType, cancellationToken))
        {
            throw new NotSupportedException(
                $"Generated plugin server context '{contextType.ToDisplayString()}' must be declared partial.");
        }

        if (serverType.DeclaredAccessibility == Accessibility.Public &&
            contextType.DeclaredAccessibility != Accessibility.Public)
        {
            throw new NotSupportedException(
                $"Generated plugin server context '{contextType.ToDisplayString()}' must be public because '{serverType.Name}' is public.");
        }
    }

    private static bool IsPartialClass(INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is ClassDeclarationSyntax declaration &&
                declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsFileLocalClass(INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is ClassDeclarationSyntax declaration &&
                declaration.Modifiers.Any(SyntaxKind.FileKeyword))
            {
                return true;
            }
        }

        return false;
    }

    private static void EnsureSingleServerOwnsContext(
        INamedTypeSymbol serverType,
        INamedTypeSymbol contextType,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        foreach (var candidate in compilation.GetSymbolsWithName(static _ => true, SymbolFilter.Type, cancellationToken))
        {
            if (candidate is not INamedTypeSymbol candidateType ||
                SymbolEqualityComparer.Default.Equals(candidateType, serverType) ||
                GeneratePluginServerAttribute(candidateType) is not { } attribute ||
                ContextType(attribute) is not { } candidateContext ||
                !SymbolEqualityComparer.Default.Equals(candidateContext, contextType))
            {
                continue;
            }

            throw new NotSupportedException(
                $"Generated plugin server context '{contextType.ToDisplayString()}' is already used by '{candidateType.ToDisplayString()}'. Each generated server must declare its own context type.");
        }
    }

    private static void ValidateContextFactory(INamedTypeSymbol contextType, string factoryName)
    {
        var methods = contextType.GetMembers(factoryName).OfType<IMethodSymbol>()
            .Where(static method => method.MethodKind == MethodKind.Ordinary)
            .ToArray();
        if (methods.Length == 0)
        {
            throw new NotSupportedException(
                $"ContextFactory '{factoryName}' was not found on '{contextType.ToDisplayString()}'.");
        }

        if (methods.Length > 1)
        {
            throw new NotSupportedException(
                $"ContextFactory '{factoryName}' on '{contextType.ToDisplayString()}' must not be overloaded.");
        }

        var method = methods[0];
        if (!method.IsStatic ||
            method.IsGenericMethod ||
            method.Parameters.Length != 1 ||
            !string.Equals(
                method.Parameters[0].Type.ToDisplayString(),
                DotBoxDMetadataNames.HookContextType,
                StringComparison.Ordinal) ||
            !SymbolEqualityComparer.Default.Equals(method.ReturnType, contextType))
        {
            throw new NotSupportedException(
                $"ContextFactory '{factoryName}' on '{contextType.ToDisplayString()}' must be static and have signature {contextType.Name} {factoryName}(HookContext raw).");
        }
    }
}
