using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class GeneratedRemoteHookChainFallback
{
    private const string RegistryAttributeName =
        "DotBoxD.Abstractions.GeneratedPluginServerRegistryAttribute";

    public static GeneratedRemoteHookChainTarget? Candidate(
        InvocationExpressionSyntax seed,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (seed.Expression is not MemberAccessExpressionSyntax onAccess ||
            !string.Equals(onAccess.Name.Identifier.ValueText, "On", StringComparison.Ordinal))
        {
            return null;
        }

        return RegistryTarget(onAccess.Expression, model, cancellationToken, depth: 0);
    }

    private static GeneratedRemoteHookChainTarget? RegistryTarget(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken,
        int depth)
    {
        if (depth > 8)
        {
            return null;
        }

        if (model.GetTypeInfo(expression, cancellationToken).Type is INamedTypeSymbol registryType &&
            TargetFromRegistryMarker(registryType, model.Compilation) is { } marked)
        {
            return marked;
        }

        if (expression is MemberAccessExpressionSyntax registryAccess &&
            TargetFromGeneratedServerMember(registryAccess, model, cancellationToken) is { } generated)
        {
            return generated;
        }

        return TargetFromDeclaredRegistryExpression(expression, model, cancellationToken) ??
            TargetFromLocalAlias(expression, model, cancellationToken, depth);
    }

    private static GeneratedRemoteHookChainTarget? TargetFromGeneratedServerMember(
        MemberAccessExpressionSyntax registryAccess,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var kind = registryAccess.Name.Identifier.ValueText switch
        {
            "Hooks" => GeneratedRemoteHookChainKind.Hook,
            "Subscriptions" => GeneratedRemoteHookChainKind.Subscription,
            _ => (GeneratedRemoteHookChainKind?)null
        };
        if (kind is null)
        {
            return null;
        }

        string? context = null;
        if (model.GetTypeInfo(registryAccess.Expression, cancellationToken).Type is INamedTypeSymbol serverType &&
            HasGeneratePluginServerAttribute(serverType, model.Compilation))
        {
            context = GeneratedContextTypeFullName(serverType, model.Compilation);
            if (context is null)
            {
                return null;
            }

            if (model.GetSymbolInfo(registryAccess, cancellationToken).Symbol is IPropertySymbol property &&
                property.Type is INamedTypeSymbol registryType)
            {
                return TargetFromRegistryMarker(registryType, model.Compilation) is { } marked &&
                    marked.Kind == kind.Value &&
                    string.Equals(marked.ServerContextTypeFullName, context, StringComparison.Ordinal)
                    ? marked
                    : null;
            }
        }

        context ??= ContextFromOwnedGeneratedServerExpression(registryAccess.Expression, model, cancellationToken);
        if (context is null)
        {
            return null;
        }

        return new GeneratedRemoteHookChainTarget(kind.Value, context);
    }

    private static GeneratedRemoteHookChainTarget? TargetFromDeclaredRegistryExpression(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken)
        => DeclaredTypeSyntax(expression, model, cancellationToken) is { } typeSyntax
            ? TargetFromOwnedGeneratedRegistryType(typeSyntax, model.Compilation, cancellationToken)
            : null;

    private static GeneratedRemoteHookChainTarget? TargetFromLocalAlias(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken,
        int depth)
    {
        if (expression is not IdentifierNameSyntax identifier ||
            model.GetSymbolInfo(identifier, cancellationToken).Symbol is not ILocalSymbol local)
        {
            return null;
        }

        foreach (var reference in local.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is VariableDeclaratorSyntax
                {
                    Initializer.Value: { } initializer
                })
            {
                return RegistryTarget(initializer, model, cancellationToken, depth + 1);
            }
        }

        return null;
    }

    private static string? ContextFromOwnedGeneratedServerExpression(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken)
        => DeclaredTypeSyntax(expression, model, cancellationToken) is { } typeSyntax
            ? ContextFromOwnedGeneratedServerType(typeSyntax, model.Compilation, cancellationToken)
            : null;

    private static TypeSyntax? DeclaredTypeSyntax(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (expression is not IdentifierNameSyntax identifier)
        {
            return null;
        }

        return model.GetSymbolInfo(identifier, cancellationToken).Symbol switch
        {
            IParameterSymbol parameter => ParameterTypeSyntax(parameter, cancellationToken),
            ILocalSymbol local => LocalTypeSyntax(local, cancellationToken),
            _ => null
        };
    }

    private static TypeSyntax? ParameterTypeSyntax(IParameterSymbol parameter, CancellationToken cancellationToken)
    {
        foreach (var reference in parameter.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is ParameterSyntax { Type: { } typeSyntax })
            {
                return typeSyntax;
            }
        }

        return null;
    }

    private static TypeSyntax? LocalTypeSyntax(ILocalSymbol local, CancellationToken cancellationToken)
    {
        foreach (var reference in local.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is VariableDeclaratorSyntax
                {
                    Parent: VariableDeclarationSyntax { Type: { } typeSyntax }
                } &&
                !typeSyntax.IsVar)
            {
                return typeSyntax;
            }
        }

        return null;
    }

    private static GeneratedRemoteHookChainTarget? TargetFromOwnedGeneratedRegistryType(
        TypeSyntax typeSyntax,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        foreach (var surface in OwnedGeneratedSurfaces(compilation, cancellationToken))
        {
            if (TypeMatches(typeSyntax, surface.HookRegistryName, surface.HookRegistryFullName))
            {
                return new GeneratedRemoteHookChainTarget(
                    GeneratedRemoteHookChainKind.Hook,
                    surface.ContextFullName);
            }

            if (TypeMatches(typeSyntax, surface.SubscriptionRegistryName, surface.SubscriptionRegistryFullName))
            {
                return new GeneratedRemoteHookChainTarget(
                    GeneratedRemoteHookChainKind.Subscription,
                    surface.ContextFullName);
            }
        }

        return null;
    }

    private static string? ContextFromOwnedGeneratedServerType(
        TypeSyntax typeSyntax,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        foreach (var surface in OwnedGeneratedSurfaces(compilation, cancellationToken))
        {
            if (TypeMatches(typeSyntax, surface.ServerInterfaceName, surface.ServerInterfaceFullName))
            {
                return surface.ContextFullName;
            }
        }

        return null;
    }

    private static bool TypeMatches(TypeSyntax typeSyntax, string simpleName, string fullName)
    {
        var text = typeSyntax.ToString();
        const string globalPrefix = "global::";
        if (text.StartsWith(globalPrefix, StringComparison.Ordinal))
        {
            text = text.Substring(globalPrefix.Length);
        }

        return string.Equals(text, simpleName, StringComparison.Ordinal) ||
            string.Equals(text, fullName, StringComparison.Ordinal);
    }
}
