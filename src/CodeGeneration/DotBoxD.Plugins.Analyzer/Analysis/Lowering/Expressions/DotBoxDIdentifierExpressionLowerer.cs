namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static class DotBoxDIdentifierExpressionLowerer
{
    public static DotBoxDExpressionModel Lower(
        string name,
        DotBoxDExpressionLoweringContext context)
    {
        if (context.InlinedBindings is { } bindings &&
            bindings.TryGetValue(name, out var bound))
        {
            return bound;
        }

        if (DotBoxDPatternCaptureExpressionLowerer.TryLowerIdentifier(name, context, out var capture))
        {
            return capture;
        }

        if (context.ProjectedElementName is { } projectedName &&
            string.Equals(projectedName, name, StringComparison.Ordinal))
        {
            return context.ProjectedElement!;
        }

        var liveSettings = context.LiveSettings;
        for (var i = 0; i < liveSettings.Count; i++)
        {
            var setting = liveSettings[i];
            if (string.Equals(setting.Name, name, StringComparison.Ordinal))
            {
                return new DotBoxDExpressionModel(
                    $"{DotBoxDGenerationNames.Helpers.Var}({LiteralReader.StringLiteral(name)})",
                    setting.Type,
                    false);
            }
        }

        throw new NotSupportedException($"Unsupported plugin identifier '{name}'.");
    }
}
