using System.Reflection.Emit;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Compiler.Emitters.Loops;

using static Compiler.IlEmitterPrimitives;

internal sealed class RawF64ExpressionPlan
{
    private readonly ExpressionKind _kind;
    private readonly string? _name;
    private readonly double _literal;
    private readonly RawF64ExpressionPlan? _operand;
    private readonly RawF64ExpressionPlan? _right;

    private RawF64ExpressionPlan(
        ExpressionKind kind,
        string bindingId = "",
        string? name = null,
        double literal = 0,
        RawF64ExpressionPlan? operand = null,
        RawF64ExpressionPlan? right = null,
        bool preservesNonNegative = false)
    {
        _kind = kind;
        _name = name;
        _literal = literal;
        _operand = operand;
        _right = right;
        BindingId = bindingId;
        PreservesNonNegative = preservesNonNegative;
        FuelCost = 1 + (operand?.FuelCost ?? 0) + (right?.FuelCost ?? 0);
        BindingCallCount = (operand?.BindingCallCount ?? 0) + (right?.BindingCallCount ?? 0)
            + (kind is ExpressionKind.Sqrt or ExpressionKind.Floor or ExpressionKind.Ceil or ExpressionKind.Round ? 1 : 0);
    }

    public string BindingId { get; }
    public int BindingCallCount { get; }
    public bool PreservesNonNegative { get; }
    public int FuelCost { get; }

    public static bool TryCreate(
        Expression expression,
        string target,
        LocalStackKindPlanner stackPlan,
        IBindingCatalog bindings,
        IReadOnlySet<string> nonNegativeF64Locals,
        out RawF64ExpressionPlan plan)
    {
        switch (expression)
        {
            case LiteralExpression { Value: F64Value value }:
                plan = new RawF64ExpressionPlan(ExpressionKind.Literal, literal: value.Value, preservesNonNegative: value.Value >= 0);
                return true;
            case VariableExpression variable when stackPlan.LocalKind(variable.Name) == StackKind.F64:
                plan = new RawF64ExpressionPlan(ExpressionKind.Variable, name: variable.Name, preservesNonNegative: nonNegativeF64Locals.Contains(variable.Name));
                return true;
            case CallExpression call:
                return TryCreateCall(call, target, stackPlan, bindings, nonNegativeF64Locals, out plan);
            case BinaryExpression { Operator: "+" or "-" or "*" or "/" } binary:
                return TryCreateBinary(binary, target, stackPlan, bindings, nonNegativeF64Locals, out plan);
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
                il.Emit(OpCodes.Ldc_R8, _literal);
                break;
            case ExpressionKind.Variable:
                il.Emit(OpCodes.Ldloc, declare(_name!).Local);
                break;
            case ExpressionKind.Add or ExpressionKind.Sub or ExpressionKind.Mul or ExpressionKind.Div:
                _operand!.Emit(il, declare);
                _right!.Emit(il, declare);
                il.Emit(OpCodes.Call, Runtime(RawMethod(_kind)));
                break;
            default:
                _operand!.Emit(il, declare);
                il.Emit(OpCodes.Call, Runtime(RawMethod(_kind)));
                break;
        }
    }

    private static bool TryCreateBinary(
        BinaryExpression binary,
        string target,
        LocalStackKindPlanner stackPlan,
        IBindingCatalog bindings,
        IReadOnlySet<string> nonNegativeF64Locals,
        out RawF64ExpressionPlan plan)
    {
        plan = null!;
        if (!TryCreate(binary.Left, target, stackPlan, bindings, nonNegativeF64Locals, out var left) ||
            !TryCreate(binary.Right, target, stackPlan, bindings, nonNegativeF64Locals, out var right) ||
            left.BindingCallCount > 0 ||
            right.BindingCallCount > 0)
        {
            return false;
        }

        var kind = binary.Operator switch
        {
            "+" => ExpressionKind.Add,
            "-" => ExpressionKind.Sub,
            "*" => ExpressionKind.Mul,
            _ => ExpressionKind.Div
        };
        plan = new RawF64ExpressionPlan(kind, operand: left, right: right);
        return true;
    }

    private static bool TryCreateCall(
        CallExpression call,
        string target,
        LocalStackKindPlanner stackPlan,
        IBindingCatalog bindings,
        IReadOnlySet<string> nonNegativeF64Locals,
        out RawF64ExpressionPlan plan)
    {
        plan = null!;
        if (call.Arguments.Count != 1 ||
            !TryGetKind(call.Name, out var kind) ||
            !CanUseDirectIntrinsic(bindings, call.Name, kind) ||
            !TryCreate(call.Arguments[0], target, stackPlan, bindings, nonNegativeF64Locals, out var operand) ||
            operand.BindingCallCount > 0 &&
            !string.Equals(operand.BindingId, call.Name, StringComparison.Ordinal))
        {
            return false;
        }

        if (kind == ExpressionKind.Sqrt && !operand.PreservesNonNegative)
        {
            return false;
        }

        plan = new RawF64ExpressionPlan(kind, bindingId: call.Name, operand: operand, preservesNonNegative: operand.PreservesNonNegative);
        return true;
    }

    private static bool CanUseDirectIntrinsic(IBindingCatalog bindings, string id, ExpressionKind kind)
        => bindings.TryGet(id, out var binding) &&
           binding.Compiled is { Kind: "RuntimeStub" } &&
           binding.Compiled.Type == typeof(CompiledRuntime).FullName &&
           binding.Compiled.Method == BoxedMethod(kind) &&
           binding.Parameters.Count == 1 &&
           binding.Parameters[0].Equals(SandboxType.F64) &&
           binding.ReturnType.Equals(SandboxType.F64) &&
           binding.RequiredCapability is null &&
           binding.Safety == BindingSafety.PureIntrinsic &&
           binding.AuditLevel == AuditLevel.None &&
           binding.CostModel.MaxCallsPerRun is null &&
           (binding.Effects & ~(SandboxEffect.Cpu | SandboxEffect.Alloc)) == SandboxEffect.None;

    private static bool TryGetKind(string id, out ExpressionKind kind)
    {
        kind = id switch
        {
            "math.sqrt" => ExpressionKind.Sqrt,
            "math.floor" => ExpressionKind.Floor,
            "math.ceil" => ExpressionKind.Ceil,
            "math.round" => ExpressionKind.Round,
            _ => ExpressionKind.Literal
        };
        return kind is not ExpressionKind.Literal;
    }

    private static string BoxedMethod(ExpressionKind kind)
        => kind switch
        {
            ExpressionKind.Sqrt => nameof(CompiledRuntime.SqrtF64),
            ExpressionKind.Floor => nameof(CompiledRuntime.FloorF64),
            ExpressionKind.Ceil => nameof(CompiledRuntime.CeilF64),
            ExpressionKind.Round => nameof(CompiledRuntime.RoundF64),
            _ => ""
        };

    private static string RawMethod(ExpressionKind kind)
        => kind switch
        {
            ExpressionKind.Sqrt => nameof(CompiledRuntime.SqrtF64Raw),
            ExpressionKind.Floor => nameof(CompiledRuntime.FloorF64Raw),
            ExpressionKind.Ceil => nameof(CompiledRuntime.CeilF64Raw),
            ExpressionKind.Round => nameof(CompiledRuntime.RoundF64Raw),
            ExpressionKind.Add => nameof(CompiledRuntime.AddF64Raw),
            ExpressionKind.Sub => nameof(CompiledRuntime.SubF64Raw),
            ExpressionKind.Mul => nameof(CompiledRuntime.MulF64Raw),
            ExpressionKind.Div => nameof(CompiledRuntime.DivF64Raw),
            _ => ""
        };

    private enum ExpressionKind
    {
        Literal,
        Variable,
        Sqrt,
        Floor,
        Ceil,
        Round,
        Add,
        Sub,
        Mul,
        Div
    }
}
