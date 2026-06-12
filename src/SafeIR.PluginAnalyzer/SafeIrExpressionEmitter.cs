namespace SafeIR.PluginAnalyzer;

using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal sealed class SafeIrExpressionEmitter
{
    private readonly PluginKernelModel _model;
    private readonly string _eventParameterName;

    public SafeIrExpressionEmitter(PluginKernelModel model, string? eventParameterName = null)
    {
        _model = model;
        _eventParameterName = eventParameterName ?? model.EventParameterName;
    }

    public string Emit(ExpressionSyntax expression)
        => expression switch {
            ParenthesizedExpressionSyntax parenthesized => Emit(parenthesized.Expression),
            BinaryExpressionSyntax binary => EmitBinary(binary),
            IdentifierNameSyntax identifier => EmitIdentifier(identifier.Identifier.ValueText),
            MemberAccessExpressionSyntax member => EmitMemberAccess(member),
            LiteralExpressionSyntax literal => LiteralReader.LiteralExpression(literal) ?? Unsupported(expression),
            _ => Unsupported(expression)
        };

    private string EmitBinary(BinaryExpressionSyntax binary)
    {
        var op = binary.Kind() switch {
            SyntaxKind.EqualsExpression => "Eq",
            SyntaxKind.NotEqualsExpression => "Ne",
            SyntaxKind.GreaterThanOrEqualExpression => "Ge",
            SyntaxKind.GreaterThanExpression => "Gt",
            SyntaxKind.LessThanOrEqualExpression => "Le",
            SyntaxKind.LessThanExpression => "Lt",
            SyntaxKind.LogicalAndExpression => "And",
            SyntaxKind.LogicalOrExpression => "Or",
            _ => throw new NotSupportedException($"Unsupported plugin expression operator '{binary.OperatorToken.ValueText}'.")
        };
        return $"{op}({Emit(binary.Left)}, {Emit(binary.Right)})";
    }

    private string EmitMemberAccess(MemberAccessExpressionSyntax member)
    {
        if (member.Expression is IdentifierNameSyntax identifier &&
            string.Equals(identifier.Identifier.ValueText, _eventParameterName, StringComparison.Ordinal)) {
            return $"Var({LiteralReader.StringLiteral(EventVariable(member.Name.Identifier.ValueText))})";
        }

        return Unsupported(member);
    }

    private string EmitIdentifier(string name)
    {
        if (_model.LiveSettings.Any(s => string.Equals(s.Name, name, StringComparison.Ordinal))) {
            return $"Var({LiteralReader.StringLiteral(name)})";
        }

        if (name is "true" or "false") {
            return $"Bool({name})";
        }

        throw new NotSupportedException($"Unsupported plugin identifier '{name}'.");
    }

    public static string EventVariable(string name) => "e_" + name;

    private static string Unsupported(ExpressionSyntax expression)
        => throw new NotSupportedException($"Unsupported plugin expression '{expression}'.");
}
