namespace DotBoxd.Kernels.Interpreter.Internal;

using DotBoxd.Kernels;

/// <summary>
/// Applies unary and binary operators to already-evaluated operands. Extracted from
/// <see cref="ExpressionEvaluator"/> verbatim so the evaluator stays focused on the
/// async operand-evaluation flow while the operator-result semantics (numeric
/// operations, string concatenation charging, equality, and error codes) live in one
/// cohesive place. These methods are pure: operands are supplied by the caller after
/// evaluation, so they introduce no new fuel/allocation charging beyond the existing
/// <see cref="SandboxContext.CreateChargedStringConcat"/> on string <c>+</c>.
/// </summary>
internal static class OperatorEvaluator
{
    /// <summary>
    /// Applies <paramref name="unary"/>'s operator to its already-evaluated operand
    /// <paramref name="value"/>, matching <see cref="ExpressionEvaluator"/>'s original
    /// operator handling exactly.
    /// </summary>
    public static SandboxValue ApplyUnary(UnaryExpression unary, SandboxValue value)
        => unary.Operator switch
        {
            "!" => SandboxValue.FromBool(!((BoolValue)value).Value),
            "-" => SandboxNumericOperations.Negate(value),
            _ => throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "unsupported unary operator"))
        };

    /// <summary>
    /// Applies <paramref name="binary"/>'s operator to its already-evaluated operands
    /// (<paramref name="left"/> and <paramref name="right"/>, in source order). String
    /// <c>+</c> charges the concatenation through <paramref name="context"/> exactly as
    /// the original evaluator did; every other branch is a pure result computation.
    /// </summary>
    public static SandboxValue ApplyBinary(
        BinaryExpression binary,
        SandboxValue left,
        SandboxValue right,
        SandboxContext context)
        => binary.Operator switch
        {
            "+" when left is StringValue l && right is StringValue r => Concat(l.Value, r.Value, context),
            "+" => SandboxNumericOperations.Add(left, right),
            "-" => SandboxNumericOperations.Subtract(left, right),
            "*" => SandboxNumericOperations.Multiply(left, right),
            "/" => SandboxNumericOperations.Divide(left, right),
            "%" => SandboxNumericOperations.Remainder(left, right),
            "==" => SandboxValue.FromBool(Equals(left, right)),
            "!=" => SandboxValue.FromBool(!Equals(left, right)),
            "<" => SandboxNumericOperations.LessThan(left, right),
            "<=" => SandboxNumericOperations.LessThanOrEqual(left, right),
            ">" => SandboxNumericOperations.GreaterThan(left, right),
            ">=" => SandboxNumericOperations.GreaterThanOrEqual(left, right),
            _ => throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "unsupported binary operator"))
        };

    private static SandboxValue Concat(string left, string right, SandboxContext context)
    {
        var text = context.CreateChargedStringConcat(left, right);
        return SandboxValue.FromString(text);
    }
}
