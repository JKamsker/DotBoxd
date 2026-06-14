namespace DotBoxD.Kernels.Interpreter.Internal;

using DotBoxD.Kernels;

internal interface I32CallEvaluator
{
    bool CanEvaluateInt32Call(CallExpression call);

    int EvaluateInt32Call(CallExpression call);

    bool TryCreateRepeatedAddCallPlan(
        CallExpression call,
        string targetName,
        out I32RepeatedAddCallPlan plan);

    bool TryCreateInt32CallPlan(
        CallExpression call,
        InterpreterFrame frame,
        string assumedInt32Local,
        out I32ExpressionPlan plan);
}

internal readonly record struct I32RepeatedAddCallPlan(int Delta, int ExpressionFuelCost, int MaxInlineCallDepth);

internal static class I32ExpressionEvaluator
{
    public static bool CanEvaluate(
        Expression expression,
        InterpreterFrame? frame,
        I32CallEvaluator? calls = null,
        string? assumedInt32Local = null)
        => expression switch
        {
            LiteralExpression { Value: I32Value } => true,
            VariableExpression variable => frame?.CanReadInt32(variable.Name) == true ||
                                           (variable.Name == assumedInt32Local && frame?.IsInt32Local(variable.Name) == true),
            UnaryExpression { Operator: "-" } unary => CanEvaluate(unary.Operand, frame, calls, assumedInt32Local),
            BinaryExpression binary when binary.Operator is "+" or "-" or "*" or "/" or "%"
                => CanEvaluate(binary.Left, frame, calls, assumedInt32Local) &&
                   CanEvaluate(binary.Right, frame, calls, assumedInt32Local),
            CallExpression call => calls?.CanEvaluateInt32Call(call) == true,
            _ => false
        };

    public static int Evaluate(
        Expression expression,
        InterpreterFrame? frame,
        SandboxContext context,
        I32CallEvaluator? calls = null)
    {
        context.ChargeFuel(1);
        return expression switch
        {
            LiteralExpression { Value: I32Value value } => value.Value,
            VariableExpression variable => frame is not null ? frame.ReadInt32(variable.Name) : throw Unsupported(),
            UnaryExpression { Operator: "-" } unary => SandboxInt32Math.Negate(Evaluate(unary.Operand, frame, context, calls)),
            BinaryExpression binary => EvaluateBinary(binary, frame, context, calls),
            CallExpression call => calls?.EvaluateInt32Call(call) ?? throw Unsupported(),
            _ => throw Unsupported()
        };
    }

    public static int FuelCost(Expression expression)
        => expression switch
        {
            LiteralExpression { Value: I32Value } => 1,
            VariableExpression => 1,
            UnaryExpression { Operator: "-" } unary => 1 + FuelCost(unary.Operand),
            BinaryExpression binary when binary.Operator is "+" or "-" or "*" or "/" or "%"
                => 1 + FuelCost(binary.Left) + FuelCost(binary.Right),
            _ => throw Unsupported()
        };

    public static int EvaluateUnmetered(Expression expression, InterpreterFrame frame)
        => expression switch
        {
            LiteralExpression { Value: I32Value value } => value.Value,
            VariableExpression variable => frame.ReadInt32(variable.Name),
            UnaryExpression { Operator: "-" } unary => SandboxInt32Math.Negate(EvaluateUnmetered(unary.Operand, frame)),
            BinaryExpression binary => EvaluateBinaryUnmetered(binary, frame),
            _ => throw Unsupported()
        };

    private static int EvaluateBinary(
        BinaryExpression binary,
        InterpreterFrame? frame,
        SandboxContext context,
        I32CallEvaluator? calls)
    {
        var left = Evaluate(binary.Left, frame, context, calls);
        var right = Evaluate(binary.Right, frame, context, calls);
        return binary.Operator switch
        {
            "+" => SandboxInt32Math.Add(left, right),
            "-" => SandboxInt32Math.Subtract(left, right),
            "*" => SandboxInt32Math.Multiply(left, right),
            "/" => SandboxInt32Math.Divide(left, right),
            "%" => SandboxInt32Math.Remainder(left, right),
            _ => throw Unsupported()
        };
    }

    private static int EvaluateBinaryUnmetered(BinaryExpression binary, InterpreterFrame frame)
    {
        var left = EvaluateUnmetered(binary.Left, frame);
        var right = EvaluateUnmetered(binary.Right, frame);
        return binary.Operator switch
        {
            "+" => SandboxInt32Math.Add(left, right),
            "-" => SandboxInt32Math.Subtract(left, right),
            "*" => SandboxInt32Math.Multiply(left, right),
            "/" => SandboxInt32Math.Divide(left, right),
            "%" => SandboxInt32Math.Remainder(left, right),
            _ => throw Unsupported()
        };
    }

    private static SandboxRuntimeException Unsupported()
        => new(new SandboxError(SandboxErrorCode.ValidationError, "unsupported i32 expression"));
}
