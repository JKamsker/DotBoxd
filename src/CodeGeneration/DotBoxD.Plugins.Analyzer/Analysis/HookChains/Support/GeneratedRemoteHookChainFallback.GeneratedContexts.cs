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

    private static string? GeneratedContextTypeFullName(INamedTypeSymbol serverType)
        => ExplicitContextTypeFullName(serverType);

    private static string? ExplicitContextTypeFullName(INamedTypeSymbol serverType)
        => GeneratePluginServerAttribute(serverType)?
            .NamedArguments
            .FirstOrDefault(static argument => string.Equals(argument.Key, "Context", StringComparison.Ordinal))
            .Value.Value is INamedTypeSymbol contextType
            ? contextType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            : null;

    private static bool HasGeneratePluginServerAttribute(INamedTypeSymbol type)
        => GeneratePluginServerAttribute(type) is not null;

    private static AttributeData? GeneratePluginServerAttribute(INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (string.Equals(
                    attribute.AttributeClass?.ToDisplayString(),
                    DotBoxDGenerationNames.TypeNames.GeneratePluginServerAttribute,
                    StringComparison.Ordinal))
            {
                return attribute;
            }
        }

        return null;
    }
}
