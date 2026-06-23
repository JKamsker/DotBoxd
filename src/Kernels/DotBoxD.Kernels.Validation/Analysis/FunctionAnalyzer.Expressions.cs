using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Validation;

internal sealed partial class FunctionAnalyzer
{
    private SandboxType AnalyzeExpression(
        Expression expression,
        FunctionScope scope,
        ref SandboxEffect effects,
        ref bool canReorder)
    {
        effects |= SandboxEffect.Cpu;
        return expression switch
        {
            LiteralExpression literal => LiteralExpressionAnalyzer.Analyze(literal, ref effects),
            VariableExpression variable => scope.Get(variable.Name, _diagnostics, variable.Span),
            UnaryExpression unary => AnalyzeUnary(unary, scope, ref effects, ref canReorder),
            BinaryExpression binary => AnalyzeBinary(binary, scope, ref effects, ref canReorder),
            CallExpression call => AnalyzeCall(call, scope, ref effects, ref canReorder),
            _ => UnknownExpression(expression)
        };
    }

    private SandboxType UnknownExpression(Expression expression)
    {
        _diagnostics.Add(new SandboxDiagnostic("E-EXPR-UNKNOWN", $"unsupported expression '{expression.GetType().Name}'", Span: expression.Span));
        return SandboxType.Unit;
    }

    private SandboxType AnalyzeUnary(
        UnaryExpression unary,
        FunctionScope scope,
        ref SandboxEffect effects,
        ref bool canReorder)
    {
        var operand = AnalyzeExpression(unary.Operand, scope, ref effects, ref canReorder);
        if (unary.Operator == "!")
        {
            Require(operand, SandboxType.Bool, unary.Span);
            return SandboxType.Bool;
        }

        if (unary.Operator != "-")
        {
            _diagnostics.Add(new SandboxDiagnostic("E-OP-UNKNOWN", $"unknown unary operator '{unary.Operator}'", Span: unary.Span));
            return SandboxType.Unit;
        }

        return AnalyzeNumericUnary(unary, operand);
    }

    private SandboxType AnalyzeBinary(
        BinaryExpression binary,
        FunctionScope scope,
        ref SandboxEffect effects,
        ref bool canReorder)
    {
        var left = AnalyzeExpression(binary.Left, scope, ref effects, ref canReorder);
        var right = AnalyzeExpression(binary.Right, scope, ref effects, ref canReorder);
        if (binary.Operator is "==" or "!=")
        {
            Require(left, right, binary.Span);
            return SandboxType.Bool;
        }

        if (binary.Operator is "<" or "<=" or ">" or ">=")
        {
            return AnalyzeNumericBinary(binary, left, right, comparison: true);
        }

        if (binary.Operator is "&&" or "||")
        {
            Require(left, SandboxType.Bool, binary.Span);
            Require(right, SandboxType.Bool, binary.Span);
            return SandboxType.Bool;
        }

        if (binary.Operator is not ("+" or "-" or "*" or "/" or "%"))
        {
            _diagnostics.Add(new SandboxDiagnostic("E-OP-UNKNOWN", $"unknown binary operator '{binary.Operator}'", Span: binary.Span));
            return SandboxType.Unit;
        }

        return AnalyzeNumericBinary(binary, left, right, comparison: false);
    }

    private SandboxType AnalyzeNumericUnary(UnaryExpression unary, SandboxType operand)
    {
        if (IsNumeric(operand))
        {
            return operand;
        }

        _diagnostics.Add(new SandboxDiagnostic(
            "E-TYPE-MISMATCH",
            $"expected numeric operand, got {operand}",
            Span: unary.Span));
        return SandboxType.Unit;
    }

    private SandboxType AnalyzeNumericBinary(
        BinaryExpression binary,
        SandboxType left,
        SandboxType right,
        bool comparison)
    {
        if (left == right && IsNumeric(left))
        {
            return comparison ? SandboxType.Bool : left;
        }

        _diagnostics.Add(new SandboxDiagnostic(
            "E-TYPE-MISMATCH",
            $"expected matching numeric operands, got {left} and {right}",
            Span: binary.Span));
        return comparison ? SandboxType.Bool : SandboxType.Unit;
    }
}
