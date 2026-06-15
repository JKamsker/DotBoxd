namespace DotBoxD.Kernels.Compiler.Emitters;

using System.Reflection.Emit;
using DotBoxD.Kernels;
using DotBoxD.Kernels.Runtime;
using static DotBoxD.Kernels.Compiler.IlEmitterPrimitives;

// Unboxed i32 expression plan for loop fast paths: builds a small tree from an i32 expression and emits raw IL
// (via the *I32Raw runtime helpers) with no per-node fuel metering — the loop runner charges the statically
// known FuelCost in bulk instead. Shared by I32LoopFastPathEmitter and BranchedI32LoopFastPathEmitter.
internal sealed class RawI32ExpressionPlan
{
    private static readonly IReadOnlyDictionary<string, SandboxFunction> NoFunctions = new Dictionary<string, SandboxFunction>(StringComparer.Ordinal);

    private readonly ExpressionKind _kind;
    private readonly string? _name;
    private readonly int _literal;
    private readonly RawI32ExpressionPlan? _left;
    private readonly RawI32ExpressionPlan? _right;
    private readonly RawI32ExpressionPlan? _third;

    private RawI32ExpressionPlan(ExpressionKind kind, string? name = null, int literal = 0, RawI32ExpressionPlan? left = null, RawI32ExpressionPlan? right = null, RawI32ExpressionPlan? third = null, int extraFuel = 0)
    {
        _kind = kind;
        _name = name;
        _literal = literal;
        _left = left;
        _right = right;
        _third = third;
        FuelCost = 1 + extraFuel + (left?.FuelCost ?? 0) + (right?.FuelCost ?? 0) + (third?.FuelCost ?? 0);
        InstructionCost = kind is ExpressionKind.Literal or ExpressionKind.Variable
            ? 1
            : 1 + (left?.InstructionCost ?? 0) + (right?.InstructionCost ?? 0) + (third?.InstructionCost ?? 0);
    }

    public int FuelCost { get; }

    public int InstructionCost { get; }

    public static bool TryCreate(Expression expression, LocalStackKindPlanner stackPlan, IReadOnlyDictionary<string, SandboxFunction> functions, out RawI32ExpressionPlan plan)
        => TryCreate(expression, stackPlan, functions, substitutions: null, out plan);

    private static bool TryCreate(Expression expression, LocalStackKindPlanner stackPlan, IReadOnlyDictionary<string, SandboxFunction> functions, IReadOnlyDictionary<string, RawI32ExpressionPlan>? substitutions, out RawI32ExpressionPlan plan)
    {
        switch (expression)
        {
            case LiteralExpression { Value: I32Value value }:
                plan = new RawI32ExpressionPlan(ExpressionKind.Literal, literal: value.Value);
                return true;
            case VariableExpression variable when substitutions?.TryGetValue(variable.Name, out var substitution) == true:
                plan = substitution;
                return true;
            case VariableExpression variable when stackPlan.LocalKind(variable.Name) == StackKind.I32:
                plan = new RawI32ExpressionPlan(ExpressionKind.Variable, name: variable.Name);
                return true;
            case UnaryExpression { Operator: "-" } unary
                when TryCreate(unary.Operand, stackPlan, functions, substitutions, out var operand):
                plan = new RawI32ExpressionPlan(ExpressionKind.Negate, left: operand);
                return true;
            case BinaryExpression binary when binary.Operator is "+" or "-" or "*" or "/" or "%":
                return TryCreateAddRemainder(binary, stackPlan, functions, substitutions, out plan) ||
                       TryCreateBinary(binary, stackPlan, functions, substitutions, out plan);
            case CallExpression call:
                return TryCreateInlineCall(call, stackPlan, functions, out plan);
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
            case ExpressionKind.Negate:
                _left!.Emit(il, declare);
                il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.NegI32Raw)));
                break;
            case ExpressionKind.InlineCall:
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.EnterInlineCall)));
                _left!.Emit(il, declare);
                var value = il.DeclareLocal(typeof(int));
                il.Emit(OpCodes.Stloc, value);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.ExitInlineCall)));
                il.Emit(OpCodes.Ldloc, value);
                break;
            case ExpressionKind.Add:
            case ExpressionKind.Subtract:
            case ExpressionKind.Multiply:
            case ExpressionKind.Divide:
            case ExpressionKind.Remainder:
                _left!.Emit(il, declare);
                _right!.Emit(il, declare);
                il.Emit(OpCodes.Call, Runtime(RuntimeMethod(_kind)));
                break;
            case ExpressionKind.AddRemainder:
                _left!.Emit(il, declare);
                _right!.Emit(il, declare);
                _third!.Emit(il, declare);
                il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.AddRemI32Raw)));
                break;
        }
    }

    private static bool TryCreateAddRemainder(BinaryExpression binary, LocalStackKindPlanner stackPlan, IReadOnlyDictionary<string, SandboxFunction> functions, IReadOnlyDictionary<string, RawI32ExpressionPlan>? substitutions, out RawI32ExpressionPlan plan)
    {
        if (substitutions is not null)
        {
            plan = null!;
            return false;
        }

        if (binary is not
            {
                Operator: "%",
                Left: BinaryExpression { Operator: "+" } add
            } ||
            !TryCreate(add.Left, stackPlan, functions, out var left) ||
            !TryCreate(add.Right, stackPlan, functions, out var right) ||
            !TryCreate(binary.Right, stackPlan, functions, out var divisor))
        {
            plan = null!;
            return false;
        }

        plan = new RawI32ExpressionPlan(ExpressionKind.AddRemainder, left: left, right: right, third: divisor, extraFuel: 1);
        return true;
    }

    private static bool TryCreateBinary(BinaryExpression binary, LocalStackKindPlanner stackPlan, IReadOnlyDictionary<string, SandboxFunction> functions, IReadOnlyDictionary<string, RawI32ExpressionPlan>? substitutions, out RawI32ExpressionPlan plan)
    {
        if (!TryCreate(binary.Left, stackPlan, functions, substitutions, out var left) ||
            !TryCreate(binary.Right, stackPlan, functions, substitutions, out var right))
        {
            plan = null!;
            return false;
        }

        plan = new RawI32ExpressionPlan(BinaryKind(binary.Operator), left: left, right: right);
        return true;
    }

    private static bool TryCreateInlineCall(CallExpression call, LocalStackKindPlanner stackPlan, IReadOnlyDictionary<string, SandboxFunction> functions, out RawI32ExpressionPlan plan)
    {
        plan = null!;
        if (!functions.TryGetValue(call.Name, out var function) ||
            !TryGetInlineableInt32Return(function, call, out var expression) ||
            !TryCreate(call.Arguments[0], stackPlan, functions, out var argument))
        {
            return false;
        }

        var substitutions = new Dictionary<string, RawI32ExpressionPlan>(StringComparer.Ordinal) { [function.Parameters[0].Name] = argument };
        if (TryCreate(expression, stackPlan, NoFunctions, substitutions, out var body))
        {
            plan = new RawI32ExpressionPlan(ExpressionKind.InlineCall, left: body, extraFuel: 3);
            return true;
        }

        return false;
    }

    private static bool TryGetInlineableInt32Return(SandboxFunction function, CallExpression call, out Expression expression)
    {
        var inlineable = call.Arguments.Count == 1 &&
                         IsSimpleInlineArgument(call.Arguments[0]) &&
                         function.Parameters.Count == 1 &&
                         function.Parameters[0].Type == SandboxType.I32 &&
                         function.ReturnType == SandboxType.I32 &&
                         function.Body.Count == 1 &&
                         function.Body[0] is ReturnStatement ret &&
                         !ContainsCall(ret.Value) &&
                         CountVariableUses(ret.Value, function.Parameters[0].Name) == 1;
        expression = inlineable ? ((ReturnStatement)function.Body[0]).Value : null!;
        return inlineable;
    }

    private static int CountVariableUses(Expression expression, string name)
        => expression switch {
            VariableExpression variable => string.Equals(variable.Name, name, StringComparison.Ordinal) ? 1 : 0,
            UnaryExpression unary => CountVariableUses(unary.Operand, name),
            BinaryExpression binary => CountVariableUses(binary.Left, name) + CountVariableUses(binary.Right, name),
            CallExpression call => call.Arguments.Sum(argument => CountVariableUses(argument, name)),
            _ => 0
        };

    private static bool IsSimpleInlineArgument(Expression expression)
        => expression is LiteralExpression { Value: I32Value } or VariableExpression;

    private static bool ContainsCall(Expression expression)
        => expression switch {
            CallExpression => true,
            UnaryExpression unary => ContainsCall(unary.Operand),
            BinaryExpression binary => ContainsCall(binary.Left) || ContainsCall(binary.Right),
            _ => false
        };

    private static ExpressionKind BinaryKind(string op)
        => op switch
        {
            "+" => ExpressionKind.Add,
            "-" => ExpressionKind.Subtract,
            "*" => ExpressionKind.Multiply,
            "/" => ExpressionKind.Divide,
            "%" => ExpressionKind.Remainder,
            _ => throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "unsupported i32 expression"))
        };

    private static string RuntimeMethod(ExpressionKind kind)
        => kind switch
        {
            ExpressionKind.Add => nameof(CompiledRuntime.AddI32Raw),
            ExpressionKind.Subtract => nameof(CompiledRuntime.SubI32Raw),
            ExpressionKind.Multiply => nameof(CompiledRuntime.MulI32Raw),
            ExpressionKind.Divide => nameof(CompiledRuntime.DivI32Raw),
            ExpressionKind.Remainder => nameof(CompiledRuntime.RemI32Raw),
            _ => throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "unsupported i32 expression"))
        };

    private enum ExpressionKind
    {
        Literal,
        Variable,
        Negate,
        InlineCall,
        Add,
        Subtract,
        Multiply,
        Divide,
        Remainder,
        AddRemainder
    }
}
