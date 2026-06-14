namespace DotBoxd.Kernels.Compiler.Emitters;

using System.Reflection.Emit;
using DotBoxd.Kernels;
using DotBoxd.Kernels.Runtime;
using static DotBoxd.Kernels.Compiler.IlEmitterPrimitives;

internal static class ListGetI32LoopFastPathEmitter
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

        if (TryGetListGetCall(assignment.Value, stackPlan, out var source, out var index))
        {
            plan = new LoopPlan(assignment.Name, source, index, AddToTarget: false, LoopFuelPerIteration: LoopFuel + 2 + index.FuelCost);
            return true;
        }

        if (assignment.Value is BinaryExpression { Operator: "+" } add &&
            TryGetAccumulatingGet(add, assignment.Name, stackPlan, out source, out index))
        {
            plan = new LoopPlan(assignment.Name, source, index, AddToTarget: true, LoopFuelPerIteration: LoopFuel + 4 + index.FuelCost);
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
        var reader = il.DeclareLocal(typeof(object));
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

        CompiledMeterEmitter.Fuel(il, 1);
        il.Emit(OpCodes.Ldloc, declare(plan.Source).Local);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.ListI32ReaderRaw)));
        il.Emit(OpCodes.Stloc, reader);
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

        CompiledMeterEmitter.Fuel(il, 1);
        if (!TryEmitRemainderCycle(range, plan, iterations, readFuel, reader, fallback, finish, il, declare))
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, readFuel);
            il.Emit(OpCodes.Ldloc, iterations);
            il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.CanBulkChargeFuel)));
            il.Emit(OpCodes.Brfalse, fallback);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, readFuel);
            il.Emit(OpCodes.Ldloc, iterations);
            il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.ChargeBulkFuel)));
            EmitLoopBody(fastLoop, finish, index, end, readFuel, reader, range.LocalName, plan, il, declare, chargeReadFuel: false);
        }

        il.MarkLabel(fallback);
        EmitLoopBody(fallbackLoop, finish, index, end, readFuel, reader, range.LocalName, plan, il, declare, chargeReadFuel: true);
        il.MarkLabel(finish);
    }

    private static bool TryEmitRemainderCycle(
        ForRangeStatement range,
        LoopPlan plan,
        LocalBuilder iterations,
        LocalBuilder readFuel,
        LocalBuilder reader,
        Label fallback,
        Label finish,
        ILGenerator il,
        Func<string, (LocalBuilder Local, StackKind Kind)> declare)
    {
        if (!plan.AddToTarget ||
            range.Start is not LiteralExpression { Value: I32Value { Value: 0 } } ||
            !plan.Index.TryGetVariableRemainderConstant(out var name, out var divisor) ||
            !string.Equals(name, range.LocalName, StringComparison.Ordinal) ||
            divisor <= 0)
        {
            return false;
        }

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iterations);
        EmitInt32(il, plan.LoopFuelPerIteration);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.CanBulkChargeLoopIterations)));
        il.Emit(OpCodes.Brfalse, fallback);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, readFuel);
        il.Emit(OpCodes.Ldloc, iterations);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.CanBulkChargeFuel)));
        il.Emit(OpCodes.Brfalse, fallback);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, reader);
        il.Emit(OpCodes.Ldloc, declare(plan.Target).Local);
        il.Emit(OpCodes.Ldloc, iterations);
        EmitInt32(il, divisor);
        EmitInt32(il, plan.LoopFuelPerIteration);
        il.Emit(OpCodes.Ldloc, readFuel);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.ListI32ReaderAddRemainderCycleFromZeroRaw)));
        il.Emit(OpCodes.Stloc, declare(plan.Target).Local);

        il.Emit(OpCodes.Ldloc, iterations);
        EmitInt32(il, 1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, declare(range.LocalName).Local);
        il.Emit(OpCodes.Br, finish);
        return true;
    }

    private static void EmitLoopBody(
        Label loop,
        Label finish,
        LocalBuilder index,
        LocalBuilder end,
        LocalBuilder readFuel,
        LocalBuilder reader,
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
            EmitListGet(plan, reader, il, declare);
            il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.AddI32Raw)));
        }
        else
        {
            EmitListGet(plan, reader, il, declare);
        }

        il.Emit(OpCodes.Stloc, declare(plan.Target).Local);
        il.Emit(OpCodes.Ldloc, index);
        EmitInt32(il, 1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, index);
        il.Emit(OpCodes.Br, loop);
    }

    private static void EmitListGet(
        LoopPlan plan,
        LocalBuilder reader,
        ILGenerator il,
        Func<string, (LocalBuilder Local, StackKind Kind)> declare)
    {
        il.Emit(OpCodes.Ldloc, reader);
        if (plan.Index.TryGetVariableRemainderConstant(out var name, out var divisor))
        {
            il.Emit(OpCodes.Ldloc, declare(name).Local);
            EmitInt32(il, divisor);
            il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.ListI32ReaderGetRemainderRaw)));
            return;
        }

        plan.Index.Emit(il, declare);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.ListI32ReaderGetRaw)));
    }

    private static bool TryGetAccumulatingGet(
        BinaryExpression expression,
        string target,
        LocalStackKindPlanner stackPlan,
        out string source,
        out I32IndexExpressionPlan index)
    {
        source = "";
        index = null!;
        return (IsTargetVariable(expression.Left, target) &&
                TryGetListGetCall(expression.Right, stackPlan, out source, out index)) ||
               (IsTargetVariable(expression.Right, target) &&
                TryGetListGetCall(expression.Left, stackPlan, out source, out index));
    }

    private static bool TryGetListGetCall(
        Expression expression,
        LocalStackKindPlanner stackPlan,
        out string source,
        out I32IndexExpressionPlan index)
    {
        source = "";
        index = null!;
        if (expression is not CallExpression { Name: "list.get", Arguments.Count: 2 } call ||
            call.Arguments[0] is not VariableExpression variable ||
            stackPlan.LocalKind(variable.Name) != StackKind.Boxed ||
            !I32IndexExpressionPlan.TryCreate(call.Arguments[1], stackPlan, out index))
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

    private readonly record struct LoopPlan(string Target, string Source, I32IndexExpressionPlan Index, bool AddToTarget, int LoopFuelPerIteration);
}
