using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static partial class DotBoxDExpressionModelFactory
{
    private static DotBoxDExpressionModel LowerThisMemberAccess(
        MemberAccessExpressionSyntax member,
        string memberName,
        DotBoxDExpressionLoweringContext context)
    {
        if (context.SemanticModel.GetSymbolInfo(member, context.CancellationToken).Symbol
            is not IPropertySymbol property)
        {
            if (context.InlinedBindings is { } bindings &&
                bindings.TryGetValue(memberName, out var bound))
            {
                return bound;
            }

            return Unsupported(member);
        }

        if (DotBoxDHostBindingExpressionLowerer.TryLowerProperty(property, context) is { } hostProperty)
        {
            return hostProperty;
        }

        var symbolKey = LiveSettingSymbolKey(property);
        var liveSettings = context.LiveSettings;
        for (var i = 0; i < liveSettings.Count; i++)
        {
            var setting = liveSettings[i];
            if (string.Equals(setting.SymbolKey, symbolKey, StringComparison.Ordinal))
            {
                return new DotBoxDExpressionModel(
                    $"{DotBoxDGenerationNames.Helpers.Var}({LiteralReader.StringLiteral(setting.Name)})",
                    setting.Type,
                    false);
            }
        }

        if (context.InlinedBindings is { } fallbackBindings &&
            fallbackBindings.TryGetValue(memberName, out var fallbackBound))
        {
            return fallbackBound;
        }

        return Unsupported(member);
    }

    private static string LiveSettingSymbolKey(IPropertySymbol property)
        => property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
            "." + property.MetadataName;
}
