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
