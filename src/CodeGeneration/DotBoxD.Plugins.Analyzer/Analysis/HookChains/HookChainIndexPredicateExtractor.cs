using System.Globalization;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

/// <summary>
/// Mines host-readable index metadata from the <c>.Where(...)</c> stages of a lowered hook chain — issue
/// #47. It extracts every leaf of the form <c>event-property &lt;comparison&gt; compile-time-constant</c>
/// reachable purely through top-level <c>&amp;&amp;</c> conjunction, so each extracted predicate is a
/// <i>necessary</i> condition of the real <c>ShouldHandle</c>: a host may reject an event on any of them
/// safely, regardless of coverage. The result also reports whether the index <i>fully</i> covers the
/// predicate (a pure AND of indexable leaves with no <c>Select</c> in front of any <c>Where</c>); only
/// then may a host skip the verified IR. Anything it cannot prove indexable (<c>||</c>, <c>!</c>,
/// non-constant captures, projected values, comparisons between two properties, unsupported types) is
/// conservatively left to the verified IR and forces partial coverage.
/// </summary>
internal static class HookChainIndexPredicateExtractor
{
    public static (EquatableArray<IndexPredicateModel> Predicates, bool FullyCovered) Extract(
        IReadOnlyList<HookChainStage> stages,
        EquatableArray<EventPropertyModel> eventProperties,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var predicates = new List<IndexPredicateModel>();
        var fullyCovered = true;
        var sawSelect = false;

        foreach (var stage in stages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stage.IsSelect)
            {
                // After a projection the flowing element is no longer the event, so later .Where()
                // predicates can't be attributed to event properties for indexing.
                sawSelect = true;
                continue;
            }

            if (sawSelect)
            {
                fullyCovered = false;
                continue;
            }

            var (elementParam, _) = LambdaParameters(stage.Lambda);
            if (elementParam is null || stage.Lambda.ExpressionBody is not { } body)
            {
                fullyCovered = false;
                continue;
            }

            CollectConjunction(body, elementParam, eventProperties, model, cancellationToken, predicates, ref fullyCovered);
        }

        return predicates.Count == 0
            ? (default, false)
            : (EquatableArray<IndexPredicateModel>.FromOwned([.. predicates]), fullyCovered);
    }

    /// <summary>
    /// Mines the same index metadata from a kernel-class <c>ShouldHandle</c> body (issue #51, candidate 1):
    /// an expression-bodied predicate or a single <c>return &lt;expr&gt;;</c> is treated exactly like a
    /// <c>.Where(...)</c> lambda — every <c>event-property &lt;op&gt; constant</c> leaf reachable through
    /// top-level <c>&amp;&amp;</c> becomes a necessary index condition, and full coverage means the whole
    /// predicate is that conjunction. Any other shape (block bodies with statements, context reads, method
    /// calls, <c>||</c>/<c>!</c>) stays conservatively non-indexed so the verified IR remains the authority.
    /// </summary>
    public static (EquatableArray<IndexPredicateModel> Predicates, bool FullyCovered) ExtractFromShouldHandle(
        MethodDeclarationSyntax shouldHandle,
        string elementParam,
        EquatableArray<EventPropertyModel> eventProperties,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var predicateExpression = PredicateExpression(shouldHandle);
        if (predicateExpression is null)
        {
            return (default, false);
        }

        var predicates = new List<IndexPredicateModel>();
        var fullyCovered = true;
        CollectConjunction(
            predicateExpression, elementParam, eventProperties, model, cancellationToken, predicates, ref fullyCovered);

        return predicates.Count == 0
            ? (default, false)
            : (EquatableArray<IndexPredicateModel>.FromOwned([.. predicates]), fullyCovered);
    }

    // The single boolean predicate a ShouldHandle reduces to, or null for any shape we won't mine: an
    // expression body (=> expr) or a block whose only statement is `return expr;`. A multi-statement body
    // is deliberately left non-indexed.
    private static ExpressionSyntax? PredicateExpression(MethodDeclarationSyntax method)
    {
        if (method.ExpressionBody?.Expression is { } expression)
        {
            return expression;
        }

        return method.Body is { Statements.Count: 1 } block &&
               block.Statements[0] is ReturnStatementSyntax { Expression: { } returned }
            ? returned
            : null;
    }

    private static void CollectConjunction(
        ExpressionSyntax expression,
        string elementParam,
        EquatableArray<EventPropertyModel> eventProperties,
        SemanticModel model,
        CancellationToken cancellationToken,
        List<IndexPredicateModel> predicates,
        ref bool fullyCovered)
    {
        var node = Unwrap(expression);
        if (node is BinaryExpressionSyntax { RawKind: (int)SyntaxKind.LogicalAndExpression } and)
        {
            CollectConjunction(and.Left, elementParam, eventProperties, model, cancellationToken, predicates, ref fullyCovered);
            CollectConjunction(and.Right, elementParam, eventProperties, model, cancellationToken, predicates, ref fullyCovered);
            return;
        }

        if (TryExtractComparison(node, elementParam, eventProperties, model, cancellationToken, out var predicate))
        {
            predicates.Add(predicate);
            return;
        }

        // A leaf we cannot turn into an index check (OR, NOT, non-constant, projected value, two
        // properties, unsupported type). It stays a necessary part of the verified IR, so the index is
        // only a partial cover.
        fullyCovered = false;
    }

    private static bool TryExtractComparison(
        ExpressionSyntax expression,
        string elementParam,
        EquatableArray<EventPropertyModel> eventProperties,
        SemanticModel model,
        CancellationToken cancellationToken,
        out IndexPredicateModel predicate)
    {
        predicate = null!;
        if (expression is not BinaryExpressionSyntax binary || !IsComparison(binary.Kind()))
        {
            return false;
        }

        var left = Unwrap(binary.Left);
        var right = Unwrap(binary.Right);

        EventPropertyModel? property;
        ExpressionSyntax constantOperand;
        bool constantOnLeft;
        if (TryResolveEventProperty(left, elementParam, eventProperties, out property))
        {
            constantOperand = right;
            constantOnLeft = false;
        }
        else if (TryResolveEventProperty(right, elementParam, eventProperties, out property))
        {
            constantOperand = left;
            constantOnLeft = true;
        }
        else
        {
            return false;
        }

        // The opposite operand must be a compile-time constant; runtime captures are not index values.
        var constant = model.GetConstantValue(constantOperand, cancellationToken);
        if (!constant.HasValue)
        {
            return false;
        }

        if (TryFormatValue(property!.Type, constant.Value, out var literal, out var valueType) is false)
        {
            return false;
        }

        predicate = new IndexPredicateModel(
            property!.Name,
            OperatorName(binary.Kind(), constantOnLeft),
            literal,
            valueType);
        return true;
    }

    private static bool TryResolveEventProperty(
        ExpressionSyntax expression,
        string elementParam,
        EquatableArray<EventPropertyModel> eventProperties,
        out EventPropertyModel? property)
    {
        property = null;
        if (expression is not MemberAccessExpressionSyntax member ||
            member.Expression is not IdentifierNameSyntax identifier ||
            !string.Equals(identifier.Identifier.ValueText, elementParam, StringComparison.Ordinal))
        {
            return false;
        }

        var name = member.Name.Identifier.ValueText;
        for (var i = 0; i < eventProperties.Count; i++)
        {
            if (string.Equals(eventProperties[i].Name, name, StringComparison.Ordinal))
            {
                property = eventProperties[i];
                return true;
            }
        }

        return false;
    }

    // Coerces the captured constant to the event property's manifest type and produces the C# literal the
    // emitter writes (so the boxed IndexedPredicate.Value has the property's runtime type). Mirrors
    // DotBoxDConstantExpressionLowerer's type rules. Returns false for anything not index-eligible.
    private static bool TryFormatValue(string propertyType, object? value, out string literal, out string valueType)
    {
        literal = string.Empty;
        valueType = propertyType;
        switch (propertyType)
        {
            case DotBoxDGenerationNames.ManifestTypes.Bool when value is bool boolean:
                literal = boolean
                    ? DotBoxDGenerationNames.CSharpLiterals.True
                    : DotBoxDGenerationNames.CSharpLiterals.False;
                return true;
            case DotBoxDGenerationNames.ManifestTypes.Int when value is int number:
                literal = number.ToString(CultureInfo.InvariantCulture);
                return true;
            case DotBoxDGenerationNames.ManifestTypes.Long when value is int number:
                literal = ((long)number).ToString(CultureInfo.InvariantCulture) +
                    DotBoxDGenerationNames.CSharpLiterals.Int64Suffix;
                return true;
            case DotBoxDGenerationNames.ManifestTypes.Long when value is long number:
                literal = number.ToString(CultureInfo.InvariantCulture) +
                    DotBoxDGenerationNames.CSharpLiterals.Int64Suffix;
                return true;
            case DotBoxDGenerationNames.ManifestTypes.Double when value is int number:
                literal = Double((double)number);
                return true;
            case DotBoxDGenerationNames.ManifestTypes.Double when value is long number:
                literal = Double((double)number);
                return true;
            case DotBoxDGenerationNames.ManifestTypes.Double when value is double number && IsFinite(number):
                literal = Double(number);
                return true;
            case DotBoxDGenerationNames.ManifestTypes.String when value is string text:
                literal = LiteralReader.StringLiteral(text);
                return true;
            default:
                return false;
        }
    }

    private static string Double(double value)
        => value.ToString(DotBoxDGenerationNames.CSharpLiterals.DoubleRoundTripFormat, CultureInfo.InvariantCulture) +
           DotBoxDGenerationNames.CSharpLiterals.DoubleSuffix;

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static bool IsComparison(SyntaxKind kind)
        => kind is SyntaxKind.EqualsExpression
            or SyntaxKind.NotEqualsExpression
            or SyntaxKind.GreaterThanExpression
            or SyntaxKind.GreaterThanOrEqualExpression
            or SyntaxKind.LessThanExpression
            or SyntaxKind.LessThanOrEqualExpression;

    // Normalizes so the event property is always the left operand: "5 <= e.Damage" reports the same
    // operator as "e.Damage >= 5". The strings are the DotBoxD.Plugins.IndexPredicateOperator member
    // names verbatim (used both to serialize and to emit IndexPredicateOperator.<name> source).
    private static string OperatorName(SyntaxKind kind, bool constantOnLeft)
        => kind switch
        {
            SyntaxKind.EqualsExpression => "Equals",
            SyntaxKind.NotEqualsExpression => "NotEquals",
            SyntaxKind.GreaterThanExpression => constantOnLeft ? "LessThan" : "GreaterThan",
            SyntaxKind.GreaterThanOrEqualExpression => constantOnLeft ? "LessThanOrEqual" : "GreaterThanOrEqual",
            SyntaxKind.LessThanExpression => constantOnLeft ? "GreaterThan" : "LessThan",
            SyntaxKind.LessThanOrEqualExpression => constantOnLeft ? "GreaterThanOrEqual" : "LessThanOrEqual",
            _ => throw new NotSupportedException(),
        };

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        var current = expression;
        while (current is ParenthesizedExpressionSyntax parenthesized)
        {
            current = parenthesized.Expression;
        }

        return current;
    }

    private static (string? ElementParam, string? ContextParam) LambdaParameters(LambdaExpressionSyntax lambda)
        => lambda switch
        {
            SimpleLambdaExpressionSyntax simple => (simple.Parameter.Identifier.ValueText, null),
            ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.ParameterList.Parameters.Count switch
            {
                1 => (parenthesized.ParameterList.Parameters[0].Identifier.ValueText, null),
                2 => (parenthesized.ParameterList.Parameters[0].Identifier.ValueText,
                      parenthesized.ParameterList.Parameters[1].Identifier.ValueText),
                _ => (null, null),
            },
            _ => (null, null),
        };
}
