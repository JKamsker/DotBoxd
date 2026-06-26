using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal sealed partial class InvokeAsyncCallShape
{
    private static void ValidateImplicitCaptureMutations(
        BlockSyntax block,
        IReadOnlyList<ImplicitCapture> captures,
        SemanticModel model)
    {
        string? CapturedName(ExpressionSyntax expression)
        {
            if (expression is not IdentifierNameSyntax ||
                model.GetSymbolInfo(expression).Symbol is not { } symbol)
            {
                return null;
            }

            foreach (var capture in captures)
            {
                if (SymbolEqualityComparer.Default.Equals(capture.Symbol, symbol) &&
                    IsMutableCollection(capture.Type))
                {
                    return capture.Name;
                }
            }

            return null;
        }

        ValidateMutableCaptureMutations(block, model, CapturedName);
    }

    private static void ValidateExplicitCaptureMutations(
        BlockSyntax block,
        InvokeAsyncCaptureParameter captureParameter,
        SemanticModel model)
    {
        string? CapturedName(ExpressionSyntax expression)
        {
            if (!TryCaptureMember(expression, captureParameter.Name, model, out _, out var target))
            {
                return null;
            }

            return IsMutableCollection(TypeOf(expression, model)) ? target.Name : null;
        }

        ValidateMutableCaptureMutations(block, model, CapturedName);
    }

    private static void ValidateMutableCaptureMutations(
        BlockSyntax block,
        SemanticModel model,
        Func<ExpressionSyntax, string?> capturedName)
    {
        var aliases = MutableCaptureAliases(block, model, capturedName);
        foreach (var invocation in block.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is MemberAccessExpressionSyntax
                {
                    Name.Identifier.ValueText: "Add"
                } member &&
                MutableCaptureName(member.Expression, model, capturedName, aliases) is { } name)
            {
                throw new NotSupportedException(
                    $"InvokeAsync captured collection '{name}' cannot be mutated in place; assign a new collection value so it can be synchronized.");
            }
        }

        foreach (var assignment in block.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.Kind() == SyntaxKind.SimpleAssignmentExpression &&
                assignment.Left is ElementAccessExpressionSyntax element &&
                MutableCaptureName(element.Expression, model, capturedName, aliases) is { } name)
            {
                throw new NotSupportedException(
                    $"InvokeAsync captured map '{name}' cannot be mutated in place; assign a new map value so it can be synchronized.");
            }
        }
    }

    private static Dictionary<ISymbol, string> MutableCaptureAliases(
        BlockSyntax block,
        SemanticModel model,
        Func<ExpressionSyntax, string?> capturedName)
    {
        var aliases = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
        foreach (var declarator in block.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (declarator.Initializer?.Value is not { } initializer ||
                capturedName(initializer) is not { } name ||
                model.GetDeclaredSymbol(declarator) is not ILocalSymbol local)
            {
                continue;
            }

            aliases[local] = name;
        }

        return aliases;
    }

    private static string? MutableCaptureName(
        ExpressionSyntax expression,
        SemanticModel model,
        Func<ExpressionSyntax, string?> capturedName,
        IReadOnlyDictionary<ISymbol, string> aliases)
    {
        if (capturedName(expression) is { } direct)
        {
            return direct;
        }

        return expression is IdentifierNameSyntax &&
               model.GetSymbolInfo(expression).Symbol is { } symbol &&
               aliases.TryGetValue(symbol, out var alias)
            ? alias
            : null;
    }

    private static bool IsMutableCollection(ITypeSymbol type)
        => DotBoxDRpcTypeMapper.ListElementType(type) is not null ||
           DotBoxDRpcTypeMapper.MapTypes(type) is not null;

    private static ITypeSymbol TypeOf(ExpressionSyntax expression, SemanticModel model)
        => model.GetTypeInfo(expression).ConvertedType ??
           model.GetTypeInfo(expression).Type ??
           throw new NotSupportedException($"InvokeAsync expression '{expression}' has no supported type.");
}
