using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering;

internal static partial class DotBoxDPatternExpressionLowerer
{
    private static DotBoxDExpressionModel LowerRecursive(
        DotBoxDExpressionModel value,
        ExpressionSyntax valueSyntax,
        RecursivePatternSyntax pattern,
        DotBoxDExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression,
        out string? captureName,
        out PatternCaptureBinding? capture)
    {
        if (pattern.Type is null)
        {
            throw new NotSupportedException($"Unsupported plugin pattern '{pattern}'.");
        }

        var lowered = LowerSubtypeTest(value, valueSyntax, pattern.Type, pattern, context, out var subtype);
        var handle = ResolveHandle(value, valueSyntax, pattern.Type, pattern, context);
        lowered = LowerPropertySubpatterns(value, valueSyntax, pattern, handle, context, lowerExpression, lowered);

        captureName = null;
        capture = null;
        if (pattern.Designation is SingleVariableDesignationSyntax designation)
        {
            captureName = designation.Identifier.ValueText;
            capture = new PatternCaptureBinding(value, subtype);
        }
        else if (pattern.Designation is not null and not DiscardDesignationSyntax)
        {
            throw new NotSupportedException($"Unsupported plugin pattern '{pattern}'.");
        }

        return lowered;
    }

    private static DotBoxDExpressionModel LowerPropertySubpatterns(
        DotBoxDExpressionModel key,
        ExpressionSyntax valueSyntax,
        RecursivePatternSyntax pattern,
        PolymorphicHandleMetadata handle,
        DotBoxDExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression,
        DotBoxDExpressionModel lowered)
    {
        if (pattern.PropertyPatternClause is not { } propertyClause)
        {
            return lowered;
        }

        foreach (var subpattern in propertyClause.Subpatterns)
        {
            if (!string.Equals(SubpatternName(subpattern), handle.KeyMember, StringComparison.Ordinal))
            {
                throw new NotSupportedException($"Unsupported plugin pattern '{pattern}'.");
            }

            var property = LowerPattern(key, valueSyntax, subpattern.Pattern, context, lowerExpression);
            lowered = And(lowered, property);
        }

        return lowered;
    }

    private static DotBoxDExpressionModel And(DotBoxDExpressionModel left, DotBoxDExpressionModel right)
    {
        RequireBool(left, DotBoxDGenerationNames.Operators.LogicalAnd);
        RequireBool(right, DotBoxDGenerationNames.Operators.LogicalAnd);
        return new DotBoxDExpressionModel(
            $"{DotBoxDGenerationNames.Helpers.And}({left.Source}, {right.Source})",
            DotBoxDGenerationNames.ManifestTypes.Bool,
            left.Allocates || right.Allocates);
    }

    private static string? SubpatternName(SubpatternSyntax subpattern)
        => subpattern.NameColon?.Name.Identifier.ValueText;

    private static PolymorphicHandleMetadata ResolveHandle(
        DotBoxDExpressionModel value,
        ExpressionSyntax valueSyntax,
        TypeSyntax typeSyntax,
        CSharpSyntaxNode pattern,
        DotBoxDExpressionLoweringContext context)
    {
        if (context.SemanticModel.GetTypeInfo(valueSyntax, context.CancellationToken).Type is { } handleType &&
            context.SemanticModel.GetTypeInfo(typeSyntax, context.CancellationToken).Type is INamedTypeSymbol resolvedSubtype &&
            PolymorphicHandleMetadataReader.TryResolve(handleType, out var handle) &&
            handle.TrySubtype(resolvedSubtype, out _) &&
            string.Equals(value.Type, handle.KeyManifestTag, StringComparison.Ordinal))
        {
            return handle;
        }

        throw new NotSupportedException($"Unsupported plugin pattern '{pattern}'.");
    }

    private static void RequireBool(DotBoxDExpressionModel expression, string symbol)
    {
        if (!string.Equals(expression.Type, DotBoxDGenerationNames.ManifestTypes.Bool, StringComparison.Ordinal))
        {
            throw new NotSupportedException($"Operator '{symbol}' requires bool operands.");
        }
    }
}
