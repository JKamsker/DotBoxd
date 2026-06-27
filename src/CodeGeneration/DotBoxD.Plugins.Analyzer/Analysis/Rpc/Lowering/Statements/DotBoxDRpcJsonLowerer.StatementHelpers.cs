using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal sealed partial class DotBoxDRpcJsonLowerer
{
    private string IncrementStatement(IdentifierNameSyntax target, SyntaxKind kind)
    {
        var op = kind is SyntaxKind.PostIncrementExpression or SyntaxKind.PreIncrementExpression ? "add" : "sub";
        return SetStatement(
            target.Identifier.ValueText,
            BinaryJson(op, Var(target.Identifier.ValueText), NumericOne(TypeOf(target))));
    }

    private static string NumericOne(ITypeSymbol type)
        => type.SpecialType switch
        {
            SpecialType.System_Int32 => I32(1),
            SpecialType.System_Int64 => I64(1),
            SpecialType.System_Single or SpecialType.System_Double => FiniteDoubleLiteralJson(1),
            _ => throw new NotSupportedException(
                $"Server extension increment target type '{type.ToDisplayString()}' is not supported.")
        };

    private void LowerWhile(WhileStatementSyntax loop, List<string> output)
    {
        var body = new List<string>();
        LowerStatement(loop.Statement, body);
        output.Add(Obj(
            ("op", Str("while")),
            ("condition", LowerRepeatedCondition(loop.Condition)),
            ("body", "[" + string.Join(",", body) + "]")));
    }

    private string LowerRepeatedCondition(ExpressionSyntax condition)
    {
        var previous = _expressionPrelude;
        var prelude = new List<string>();
        _expressionPrelude = prelude;
        try
        {
            var lowered = LowerExpression(condition);
            if (prelude.Count > 0)
            {
                throw new NotSupportedException(
                    "Server extension while conditions cannot require generated temporary locals.");
            }

            return lowered;
        }
        finally
        {
            _expressionPrelude = previous;
        }
    }
}
