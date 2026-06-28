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

        expression = HookChainAliasResolver.UnwrapTransparentExpression(expression);
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

        if (expression is ConditionalExpressionSyntax conditional &&
            TargetFromConditionalRegistryExpression(conditional, model, cancellationToken, depth) is { } conditionalTarget)
        {
            return conditionalTarget;
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

        var serverExpression = HookChainAliasResolver.UnwrapTransparentExpression(registryAccess.Expression);
        string? context = null;
        if (model.GetTypeInfo(serverExpression, cancellationToken).Type is INamedTypeSymbol serverType &&
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

        context ??= ContextFromOwnedGeneratedServerExpression(serverExpression, model, cancellationToken);
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
            ? TargetFromOwnedGeneratedRegistryType(typeSyntax, model, cancellationToken)
            : null;

    private static string? ContextFromOwnedGeneratedServerExpression(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken)
        => DeclaredTypeSyntax(expression, model, cancellationToken) is { } typeSyntax
            ? ContextFromOwnedGeneratedServerType(typeSyntax, model, cancellationToken)
            : null;

    private static GeneratedRemoteHookChainTarget? TargetFromOwnedGeneratedRegistryType(
        TypeSyntax typeSyntax,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var resolvedType = TypeFromSyntax(typeSyntax, model, cancellationToken);
        if (resolvedType is { TypeKind: not TypeKind.Error })
        {
            return resolvedType is INamedTypeSymbol registryType
                ? TargetFromRegistryMarker(registryType, model.Compilation)
                : null;
        }

        foreach (var surface in OwnedGeneratedSurfaces(model.Compilation, cancellationToken))
        {
            if (TypeMatches(
                typeSyntax,
                surface.HookRegistryName,
                surface.HookRegistryFullName,
                model,
                cancellationToken))
            {
                return new GeneratedRemoteHookChainTarget(
                    GeneratedRemoteHookChainKind.Hook,
                    surface.ContextFullName);
            }

            if (TypeMatches(
                typeSyntax,
                surface.SubscriptionRegistryName,
                surface.SubscriptionRegistryFullName,
                model,
                cancellationToken))
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
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var resolvedType = TypeFromSyntax(typeSyntax, model, cancellationToken);
        if (resolvedType is { TypeKind: not TypeKind.Error })
        {
            return resolvedType is INamedTypeSymbol serverType
                ? GeneratedContextTypeFullName(serverType, model.Compilation)
                : null;
        }

        foreach (var surface in OwnedGeneratedSurfaces(model.Compilation, cancellationToken))
        {
            if (TypeMatches(
                typeSyntax,
                surface.ServerInterfaceName,
                surface.ServerInterfaceFullName,
                model,
                cancellationToken))
            {
                return surface.ContextFullName;
            }
        }

        return null;
    }

}
