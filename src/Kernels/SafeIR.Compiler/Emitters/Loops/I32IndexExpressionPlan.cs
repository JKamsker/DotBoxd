namespace SafeIR.Compiler.Emitters;

using System.Reflection.Emit;
using SafeIR;
using SafeIR.Runtime;
using static SafeIR.Compiler.IlEmitterPrimitives;

internal sealed class I32IndexExpressionPlan
{
    private readonly ExpressionKind _kind;
    private readonly string? _name;
    private readonly int _literal;
    private readonly I32IndexExpressionPlan? _left;
    private readonly I32IndexExpressionPlan? _right;

    private I32IndexExpressionPlan(
        ExpressionKind kind,
        string? name = null,
        int literal = 0,
        I32IndexExpressionPlan? left = null,
        I32IndexExpressionPlan? right = null)
    {
        _kind = kind;
        _name = name;
        _literal = literal;
        _left = left;
        _right = right;
        FuelCost = 1 + (left?.FuelCost ?? 0) + (right?.FuelCost ?? 0);
    }

    public int FuelCost { get; }

    public bool TryGetVariableRemainderConstant(out string name, out int divisor)
    {
        if (_kind == ExpressionKind.Remainder &&
            _left is { _kind: ExpressionKind.Variable, _name: { } variable } &&
            _right is { _kind: ExpressionKind.Literal } literal)
        {
            name = variable;
            divisor = literal._literal;
            return true;
        }

        name = "";
        divisor = 0;
        return false;
    }

    public static bool TryCreate(
        Expression expression,
        LocalStackKindPlanner stackPlan,
        out I32IndexExpressionPlan plan)
    {
        switch (expression)
        {
            case LiteralExpression { Value: I32Value value }:
                plan = new I32IndexExpressionPlan(ExpressionKind.Literal, literal: value.Value);
                return true;
            case VariableExpression variable when stackPlan.LocalKind(variable.Name) == StackKind.I32:
                plan = new I32IndexExpressionPlan(ExpressionKind.Variable, name: variable.Name);
                return true;
            case BinaryExpression binary when binary.Operator is "+" or "-" or "*" or "/" or "%":
                return TryCreateBinary(binary, stackPlan, out plan);
            default:
                plan = null!;
                return false;
        }
    }

    public void Emit(ILGenerator il, Func<string, (LocalBuilder Local, StackKind Kind)> declare)
    {
        switch (_kind)
        {
            case ExpressionKind.Literal:
                EmitInt32(il, _literal);
                break;
            case ExpressionKind.Variable:
                il.Emit(OpCodes.Ldloc, declare(_name!).Local);
                break;
            default:
                _left!.Emit(il, declare);
                _right!.Emit(il, declare);
                il.Emit(OpCodes.Call, Runtime(RuntimeMethod(_kind)));
                break;
        }
    }

    private static bool TryCreateBinary(
        BinaryExpression binary,
        LocalStackKindPlanner stackPlan,
        out I32IndexExpressionPlan plan)
    {
        if (!TryCreate(binary.Left, stackPlan, out var left) ||
            !TryCreate(binary.Right, stackPlan, out var right))
        {
            plan = null!;
            return false;
        }

        plan = new I32IndexExpressionPlan(BinaryKind(binary.Operator), left: left, right: right);
        return true;
    }

    private static ExpressionKind BinaryKind(string op)
        => op switch
        {
            "+" => ExpressionKind.Add,
            "-" => ExpressionKind.Subtract,
            "*" => ExpressionKind.Multiply,
            "/" => ExpressionKind.Divide,
            "%" => ExpressionKind.Remainder,
            _ => throw Unsupported()
        };

    private static string RuntimeMethod(ExpressionKind kind)
        => kind switch
        {
            ExpressionKind.Add => nameof(CompiledRuntime.AddI32Raw),
            ExpressionKind.Subtract => nameof(CompiledRuntime.SubI32Raw),
            ExpressionKind.Multiply => nameof(CompiledRuntime.MulI32Raw),
            ExpressionKind.Divide => nameof(CompiledRuntime.DivI32Raw),
            ExpressionKind.Remainder => nameof(CompiledRuntime.RemI32Raw),
            _ => throw Unsupported()
        };

    private static SandboxRuntimeException Unsupported()
        => new(new SandboxError(SandboxErrorCode.ValidationError, "unsupported i32 expression"));

    private enum ExpressionKind
    {
        Literal,
        Variable,
        Add,
        Subtract,
        Multiply,
        Divide,
        Remainder
    }
}
