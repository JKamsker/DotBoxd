namespace DotBoxD.Kernels.Compiler.Emitters;

using System.Reflection.Emit;
using DotBoxD.Kernels;
using DotBoxD.Kernels.Runtime;
using static DotBoxD.Kernels.Compiler.IlEmitterPrimitives;

// Unboxed i64 expression plan for the i64 loop fast path: literals, i64 locals, and checked i64 arithmetic,
// emitted as raw IL (the *I64Raw helpers) with no per-node metering — the loop runner bulk-charges FuelCost.
// Intentionally narrow (no inline calls / no cross-type i32 operands); anything else falls back to the general
// per-node-metered emitter.
internal sealed class RawI64ExpressionPlan
{
    private readonly ExpressionKind _kind;
    private readonly string? _name;
    private readonly long _literal;
    private readonly RawI64ExpressionPlan? _left;
    private readonly RawI64ExpressionPlan? _right;

    private RawI64ExpressionPlan(ExpressionKind kind, string? name = null, long literal = 0, RawI64ExpressionPlan? left = null, RawI64ExpressionPlan? right = null)
    {
        _kind = kind;
        _name = name;
        _literal = literal;
        _left = left;
        _right = right;
        FuelCost = 1 + (left?.FuelCost ?? 0) + (right?.FuelCost ?? 0);
    }

    public int FuelCost { get; }

    public static bool TryCreate(Expression expression, LocalStackKindPlanner stackPlan, out RawI64ExpressionPlan plan)
    {
        switch (expression)
        {
            case LiteralExpression { Value: I64Value value }:
                plan = new RawI64ExpressionPlan(ExpressionKind.Literal, literal: value.Value);
                return true;
            case VariableExpression variable when stackPlan.LocalKind(variable.Name) == StackKind.I64:
                plan = new RawI64ExpressionPlan(ExpressionKind.Variable, name: variable.Name);
                return true;
            case UnaryExpression { Operator: "-" } unary when TryCreate(unary.Operand, stackPlan, out var operand):
                plan = new RawI64ExpressionPlan(ExpressionKind.Negate, left: operand);
                return true;
            case BinaryExpression { Operator: "+" or "-" or "*" or "/" or "%" } binary
                when TryCreate(binary.Left, stackPlan, out var left) && TryCreate(binary.Right, stackPlan, out var right):
                plan = new RawI64ExpressionPlan(BinaryKind(binary.Operator), left: left, right: right);
                return true;
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
                il.Emit(OpCodes.Ldc_I8, _literal);
                break;
            case ExpressionKind.Variable:
                il.Emit(OpCodes.Ldloc, declare(_name!).Local);
                break;
            case ExpressionKind.Negate:
                _left!.Emit(il, declare);
                il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.NegI64Raw)));
                break;
            default:
                _left!.Emit(il, declare);
                _right!.Emit(il, declare);
                il.Emit(OpCodes.Call, Runtime(RuntimeMethod(_kind)));
                break;
        }
    }

    private static ExpressionKind BinaryKind(string op)
        => op switch
        {
            "+" => ExpressionKind.Add,
            "-" => ExpressionKind.Subtract,
            "*" => ExpressionKind.Multiply,
            "/" => ExpressionKind.Divide,
            "%" => ExpressionKind.Remainder,
            _ => throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "unsupported i64 expression"))
        };

    private static string RuntimeMethod(ExpressionKind kind)
        => kind switch
        {
            ExpressionKind.Add => nameof(CompiledRuntime.AddI64Raw),
            ExpressionKind.Subtract => nameof(CompiledRuntime.SubI64Raw),
            ExpressionKind.Multiply => nameof(CompiledRuntime.MulI64Raw),
            ExpressionKind.Divide => nameof(CompiledRuntime.DivI64Raw),
            ExpressionKind.Remainder => nameof(CompiledRuntime.RemI64Raw),
            _ => throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "unsupported i64 expression"))
        };

    private enum ExpressionKind
    {
        Literal,
        Variable,
        Negate,
        Add,
        Subtract,
        Multiply,
        Divide,
        Remainder
    }
}
