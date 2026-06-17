using System.Reflection.Emit;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Compiler.Emitters.Loops;

using static Compiler.IlEmitterPrimitives;

internal static class ListCountLoopFastPathEmitter
{
    private const int LoopFuel = 5;

    public static bool TryEmit(
        ForRangeStatement range,
        ILGenerator il,
        LocalStackKindPlanner stackPlan,
        Func<string, (LocalBuilder Local, StackKind Kind)> declare)
    {
        if (stackPlan.LocalKind(range.LocalName) != StackKind.I32 ||
            !CanEmitBound(range.Start, stackPlan) ||
            !CanEmitBound(range.End, stackPlan) ||
            !TryCreatePlan(range, stackPlan, out var plan))
        {
            return false;
        }

        EmitLoop(range, il, declare, plan);
        return true;
    }

    private static bool TryCreatePlan(
        ForRangeStatement range,
        LocalStackKindPlanner stackPlan,
        out LoopPlan plan)
    {
        plan = default;
        if (range.Body.Count != 1 ||
            range.Body[0] is not AssignmentStatement assignment ||
            stackPlan.LocalKind(assignment.Name) != StackKind.I32)
        {
            return false;
        }

        if (TryGetListCountCall(assignment.Value, stackPlan, out var source))
        {
            plan = new LoopPlan(assignment.Name, source, AddToTarget: false, LoopFuelPerIteration: LoopFuel + 2);
            return true;
        }

        if (assignment.Value is BinaryExpression { Operator: "+" } add &&
            TryGetAccumulatingCount(add, assignment.Name, stackPlan, out source))
        {
            plan = new LoopPlan(assignment.Name, source, AddToTarget: true, LoopFuelPerIteration: LoopFuel + 4);
            return true;
        }

        return false;
    }

    private static void EmitLoop(
        ForRangeStatement range,
        ILGenerator il,
        Func<string, (LocalBuilder Local, StackKind Kind)> declare,
        LoopPlan plan)
    {
        var index = il.DeclareLocal(typeof(int));
        var end = il.DeclareLocal(typeof(int));
        var iterations = il.DeclareLocal(typeof(int));
        var count = il.DeclareLocal(typeof(int));
        var readFuel = il.DeclareLocal(typeof(long));
        EmitBound(range.Start, il, declare);
        il.Emit(OpCodes.Stloc, index);
        EmitBound(range.End, il, declare);
        il.Emit(OpCodes.Stloc, end);

        var fallback = il.DefineLabel();
        var fastLoop = il.DefineLabel();
        var fallbackLoop = il.DefineLabel();
        var finish = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldloc, end);
        il.Emit(OpCodes.Bge, finish);

        il.Emit(OpCodes.Ldloc, declare(plan.Source).Local);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.ListCountRaw)));
        il.Emit(OpCodes.Stloc, count);
        il.Emit(OpCodes.Ldloc, count);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.ListReadFuelRaw)));
        il.Emit(OpCodes.Stloc, readFuel);
        il.Emit(OpCodes.Ldloc, end);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, iterations);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iterations);
        EmitInt32(il, plan.LoopFuelPerIteration);
        il.Emit(OpCodes.Ldloc, readFuel);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.CanBulkChargeLoopIterationsAndFuel)));
        il.Emit(OpCodes.Brfalse, fallback);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, readFuel);
        il.Emit(OpCodes.Ldloc, iterations);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.ChargeBulkFuel)));
        if (plan.AddToTarget)
        {
            // The hoisted count is loop-invariant, so "target += count" collapses to "target += count *
            // iterations" via AccumulateLinearI32 (identical loop-iteration fuel and overflow-throw point);
            // the per-element read fuel was already bulk-charged above. Mirrors the string.length fast path.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, declare(plan.Target).Local);
            il.Emit(OpCodes.Ldloc, count);
            il.Emit(OpCodes.Ldloc, iterations);
            EmitInt32(il, plan.LoopFuelPerIteration);
            il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.AccumulateLinearI32)));
            il.Emit(OpCodes.Stloc, declare(plan.Target).Local);
            il.Emit(OpCodes.Br, finish);
        }
        else
        {
            EmitLoopBody(fastLoop, finish, index, end, count, readFuel, range.LocalName, plan, il, declare, chargeReadFuel: false);
        }

        il.MarkLabel(fallback);
        EmitLoopBody(fallbackLoop, finish, index, end, count, readFuel, range.LocalName, plan, il, declare, chargeReadFuel: true);
        il.MarkLabel(finish);
    }

    private static void EmitLoopBody(
        Label loop,
        Label finish,
        LocalBuilder index,
        LocalBuilder end,
        LocalBuilder count,
        LocalBuilder readFuel,
        string loopLocal,
        LoopPlan plan,
        ILGenerator il,
        Func<string, (LocalBuilder Local, StackKind Kind)> declare,
        bool chargeReadFuel)
    {
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldloc, end);
        il.Emit(OpCodes.Bge, finish);
        CompiledMeterEmitter.LoopIteration(il, plan.LoopFuelPerIteration);
        if (chargeReadFuel)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, readFuel);
            il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.ChargeFuel64)));
        }

        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Stloc, declare(loopLocal).Local);
        if (plan.AddToTarget)
        {
            il.Emit(OpCodes.Ldloc, declare(plan.Target).Local);
            il.Emit(OpCodes.Ldloc, count);
            il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.AddI32Raw)));
        }
        else
        {
            il.Emit(OpCodes.Ldloc, count);
        }

        il.Emit(OpCodes.Stloc, declare(plan.Target).Local);
        il.Emit(OpCodes.Ldloc, index);
        EmitInt32(il, 1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, index);
        il.Emit(OpCodes.Br, loop);
    }

    private static bool TryGetAccumulatingCount(
        BinaryExpression expression,
        string target,
        LocalStackKindPlanner stackPlan,
        out string source)
    {
        source = "";
        return (IsTargetVariable(expression.Left, target) &&
                TryGetListCountCall(expression.Right, stackPlan, out source)) ||
               (IsTargetVariable(expression.Right, target) &&
                TryGetListCountCall(expression.Left, stackPlan, out source));
    }

    private static bool TryGetListCountCall(
        Expression expression,
        LocalStackKindPlanner stackPlan,
        out string source)
    {
        source = "";
        if (expression is not CallExpression { Name: "list.count", Arguments.Count: 1 } call ||
            call.Arguments[0] is not VariableExpression variable ||
            stackPlan.LocalKind(variable.Name) != StackKind.Boxed)
        {
            return false;
        }

        source = variable.Name;
        return true;
    }

    private static bool IsTargetVariable(Expression expression, string target)
        => expression is VariableExpression variable &&
           string.Equals(variable.Name, target, StringComparison.Ordinal);

    private static void EmitBound(
        Expression expression,
        ILGenerator il,
        Func<string, (LocalBuilder Local, StackKind Kind)> declare)
    {
        switch (expression)
        {
            case LiteralExpression { Value: I32Value value }:
                EmitInt32(il, value.Value);
                break;
            case VariableExpression variable:
                il.Emit(OpCodes.Ldloc, declare(variable.Name).Local);
                break;
            default:
                throw new SandboxRuntimeException(new SandboxError(
                    SandboxErrorCode.ValidationError,
                    "unsupported forRange bound"));
        }
    }

    private static bool CanEmitBound(Expression expression, LocalStackKindPlanner stackPlan)
        => expression is LiteralExpression { Value: I32Value } ||
           expression is VariableExpression variable && stackPlan.LocalKind(variable.Name) == StackKind.I32;

    private readonly record struct LoopPlan(
        string Target,
        string Source,
        bool AddToTarget,
        int LoopFuelPerIteration);
}
