using System.Reflection.Emit;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Compiler.Emitters.Loops;

using static Compiler.IlEmitterPrimitives;

internal sealed class I32ModuloIndexWhileLoopFastPathEmitter
{
    private const int ConditionFuel = 3;
    private const int LoopFuel = 15;

    private readonly ILGenerator _il;
    private readonly LocalStackKindPlanner _stackPlan;
    private readonly Func<string, (LocalBuilder Local, StackKind Kind)> _declare;

    private I32ModuloIndexWhileLoopFastPathEmitter(
        ILGenerator il,
        LocalStackKindPlanner stackPlan,
        Func<string, (LocalBuilder Local, StackKind Kind)> declare)
    {
        _il = il;
        _stackPlan = stackPlan;
        _declare = declare;
    }

    public static bool TryEmit(
        WhileStatement loop,
        ILGenerator il,
        LocalStackKindPlanner stackPlan,
        Func<string, (LocalBuilder Local, StackKind Kind)> declare)
        => new I32ModuloIndexWhileLoopFastPathEmitter(il, stackPlan, declare).TryEmit(loop);

    private bool TryEmit(WhileStatement loop)
    {
        if (!TryCreatePlan(loop, out var plan))
        {
            return false;
        }

        var end = _il.DeclareLocal(typeof(int));
        EmitBound(plan.End);
        _il.Emit(OpCodes.Stloc, end);

        var fallback = _il.DefineLabel();
        var fallbackLoop = _il.DefineLabel();
        var finish = _il.DefineLabel();
        EmitCanUse(plan, end);
        _il.Emit(OpCodes.Brfalse, fallback);
        EmitClosedForm(plan, end);
        _il.Emit(OpCodes.Ldloc, end);
        _il.Emit(OpCodes.Stloc, _declare(plan.Index).Local);
        _il.Emit(OpCodes.Br, finish);

        _il.MarkLabel(fallback);
        EmitFallbackLoop(plan, end, fallbackLoop, finish);
        _il.MarkLabel(finish);
        return true;
    }

    private void EmitCanUse(WhilePlan plan, LocalBuilder end)
    {
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, _declare(plan.Target).Local);
        _il.Emit(OpCodes.Ldloc, _declare(plan.Index).Local);
        _il.Emit(OpCodes.Ldloc, end);
        EmitInt32(_il, plan.Divisor);
        EmitInt32(_il, ConditionFuel);
        EmitInt32(_il, LoopFuel);
        _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.CanUseModuloIndexAccumulatorRaw)));
    }

    private void EmitClosedForm(WhilePlan plan, LocalBuilder end)
    {
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, _declare(plan.Target).Local);
        _il.Emit(OpCodes.Ldloc, _declare(plan.Index).Local);
        _il.Emit(OpCodes.Ldloc, end);
        EmitInt32(_il, plan.Divisor);
        EmitInt32(_il, ConditionFuel);
        EmitInt32(_il, LoopFuel);
        _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.AddModuloIndexAccumulatorI32LoopRaw)));
        _il.Emit(OpCodes.Stloc, _declare(plan.Target).Local);
    }

    private void EmitFallbackLoop(WhilePlan plan, LocalBuilder end, Label loop, Label finish)
    {
        _il.MarkLabel(loop);
        CompiledMeterEmitter.Fuel(_il, ConditionFuel);
        _il.Emit(OpCodes.Ldloc, _declare(plan.Index).Local);
        _il.Emit(OpCodes.Ldloc, end);
        _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.LtI32Raw)));
        _il.Emit(OpCodes.Brfalse, finish);
        CompiledMeterEmitter.LoopIteration(_il, LoopFuel);
        _il.Emit(OpCodes.Ldloc, _declare(plan.Target).Local);
        _il.Emit(OpCodes.Ldloc, _declare(plan.Index).Local);
        EmitInt32(_il, plan.Divisor);
        _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.AddRemI32Raw)));
        _il.Emit(OpCodes.Stloc, _declare(plan.Target).Local);
        _il.Emit(OpCodes.Ldloc, _declare(plan.Index).Local);
        EmitInt32(_il, 1);
        _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.AddI32Raw)));
        _il.Emit(OpCodes.Stloc, _declare(plan.Index).Local);
        _il.Emit(OpCodes.Br, loop);
    }

    private void EmitBound(Expression expression)
    {
        switch (expression)
        {
            case LiteralExpression { Value: I32Value value }:
                EmitInt32(_il, value.Value);
                break;
            case VariableExpression variable:
                _il.Emit(OpCodes.Ldloc, _declare(variable.Name).Local);
                break;
            default:
                throw new SandboxRuntimeException(new SandboxError(
                    SandboxErrorCode.ValidationError,
                    "unsupported while bound"));
        }
    }

    private bool TryCreatePlan(WhileStatement loop, out WhilePlan plan)
    {
        plan = default;
        if (!TryGetCondition(loop.Condition, out var index, out var end) ||
            loop.Body is not [AssignmentStatement totalAssignment, AssignmentStatement indexAssignment] ||
            _stackPlan.LocalKind(index) != StackKind.I32 ||
            !IsI32Bound(end) ||
            !TryGetModuloAssignment(totalAssignment, index, out var target, out var divisor) ||
            !TryGetIncrement(indexAssignment, index) ||
            _stackPlan.LocalKind(target) != StackKind.I32 ||
            IsAssignedBound(end, target, index) ||
            divisor <= 0)
        {
            return false;
        }

        plan = new WhilePlan(index, end, target, divisor);
        return true;
    }

    private bool IsI32Bound(Expression end)
        => end is LiteralExpression { Value: I32Value } ||
           end is VariableExpression variable && _stackPlan.LocalKind(variable.Name) == StackKind.I32;

    private static bool TryGetCondition(Expression expression, out string index, out Expression end)
    {
        index = "";
        end = null!;
        if (expression is not BinaryExpression
            {
                Operator: "<",
                Left: VariableExpression variable,
                Right: LiteralExpression { Value: I32Value } or VariableExpression
            })
        {
            return false;
        }

        index = variable.Name;
        end = ((BinaryExpression)expression).Right;
        return true;
    }

    private static bool TryGetModuloAssignment(
        AssignmentStatement assignment,
        string index,
        out string target,
        out int divisor)
    {
        target = assignment.Name;
        divisor = 0;
        return assignment.Value is BinaryExpression
        {
            Operator: "%",
            Left: BinaryExpression { Operator: "+" } add,
            Right: LiteralExpression { Value: I32Value divisorValue }
        } &&
        divisorValue.Value > 0 &&
        ((IsVariable(add.Left, target) && IsVariable(add.Right, index)) ||
         (IsVariable(add.Right, target) && IsVariable(add.Left, index))) &&
        (divisor = divisorValue.Value) > 0;
    }

    private static bool TryGetIncrement(AssignmentStatement assignment, string index)
        => string.Equals(assignment.Name, index, StringComparison.Ordinal) &&
           assignment.Value is BinaryExpression { Operator: "+" } add &&
           ((IsVariable(add.Left, index) && IsLiteral(add.Right, 1)) ||
            (IsVariable(add.Right, index) && IsLiteral(add.Left, 1)));

    private static bool IsAssignedBound(Expression end, string target, string index)
        => end is VariableExpression variable &&
           (string.Equals(variable.Name, target, StringComparison.Ordinal) ||
            string.Equals(variable.Name, index, StringComparison.Ordinal));

    private static bool IsVariable(Expression expression, string name)
        => expression is VariableExpression variable &&
           string.Equals(variable.Name, name, StringComparison.Ordinal);

    private static bool IsLiteral(Expression expression, int value)
        => expression is LiteralExpression { Value: I32Value literal } && literal.Value == value;

    private readonly record struct WhilePlan(string Index, Expression End, string Target, int Divisor);
}
