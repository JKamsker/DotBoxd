using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class GeneratedRemoteHookChainFallback
{
    public static string ServerContextTypeFullName(
        SemanticModel model,
        InvocationExpressionSyntax seed,
        GeneratedRemoteHookChainTarget target,
        CancellationToken cancellationToken)
    {
        if (seed.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax onName } &&
            onName.TypeArgumentList.Arguments.Count >= 2)
        {
            return model.GetTypeInfo(onName.TypeArgumentList.Arguments[1], cancellationToken)
                .Type?
                .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? target.ServerContextTypeFullName;
        }

        return target.ServerContextTypeFullName;
    }

    public static ITypeSymbol? ServerContextTypeForLambda(
        LambdaExpressionSyntax lambda,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        foreach (var invocation in lambda.Ancestors().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax access ||
                SeedInvocation(access.Expression) is not { } seed ||
                Candidate(seed, model, cancellationToken) is not { } target)
            {
                continue;
            }

            return ServerContextType(model, seed, target, cancellationToken);
        }

        return null;
    }

    public static INamedTypeSymbol? ServerContextType(
        SemanticModel model,
        InvocationExpressionSyntax seed,
        GeneratedRemoteHookChainTarget target,
        CancellationToken cancellationToken)
    {
        if (seed.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax onName } &&
            onName.TypeArgumentList.Arguments.Count >= 2 &&
            model.GetTypeInfo(onName.TypeArgumentList.Arguments[1], cancellationToken).Type is INamedTypeSymbol declared)
        {
            return declared;
        }

        var metadataName = target.ServerContextTypeFullName.StartsWith("global::", StringComparison.Ordinal)
            ? target.ServerContextTypeFullName.Substring("global::".Length)
            : target.ServerContextTypeFullName;
        return model.Compilation.GetTypeByMetadataName(metadataName);
    }

    private static InvocationExpressionSyntax? SeedInvocation(ExpressionSyntax expression)
    {
        for (var current = expression; current is InvocationExpressionSyntax invocation; current = NextReceiver(invocation))
        {
            if (invocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "On" })
            {
                return invocation;
            }
        }

        return null;
    }

    private static ExpressionSyntax? NextReceiver(InvocationExpressionSyntax invocation)
        => invocation.Expression is MemberAccessExpressionSyntax member
            ? member.Expression
            : null;

    private static string? GeneratedContextTypeFullName(INamedTypeSymbol serverType, Compilation compilation)
        => ExplicitContextTypeFullName(serverType, compilation);

    private static string? ExplicitContextTypeFullName(INamedTypeSymbol serverType, Compilation compilation)
        => GeneratePluginServerAttribute(serverType, compilation)?
            .NamedArguments
            .FirstOrDefault(static argument => string.Equals(argument.Key, "Context", StringComparison.Ordinal))
            .Value.Value is INamedTypeSymbol contextType
            ? contextType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            : null;

    private static bool HasGeneratePluginServerAttribute(INamedTypeSymbol type, Compilation compilation)
        => GeneratePluginServerAttribute(type, compilation) is not null;

    private static AttributeData? GeneratePluginServerAttribute(INamedTypeSymbol type, Compilation compilation)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (IsDotBoxDAttribute(
                    attribute,
                    compilation,
                    DotBoxDGenerationNames.TypeNames.GeneratePluginServerAttribute,
                    out _))
            {
                return attribute;
            }
        }

        return null;
    }

    private static bool IsDotBoxDAttribute(
        AttributeData attribute,
        Compilation compilation,
        string metadataName,
        out INamedTypeSymbol expectedType)
    {
        expectedType = compilation.GetTypeByMetadataName(metadataName)!;
        return expectedType is not null &&
            SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, expectedType);
    }
}
