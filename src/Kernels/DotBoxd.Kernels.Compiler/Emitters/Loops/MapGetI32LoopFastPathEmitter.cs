namespace DotBoxd.Kernels.Compiler.Emitters;

using System.Reflection.Emit;
using DotBoxd.Kernels;
using DotBoxd.Kernels.Runtime;
using static DotBoxd.Kernels.Compiler.IlEmitterPrimitives;

internal static class MapGetI32LoopFastPathEmitter
{
    private const int LoopFuel = 5;
    private const int LiteralFuel = 1;

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

        if (TryGetMapGetCall(assignment.Value, stackPlan, out var source, out var key))
        {
            plan = new LoopPlan(assignment.Name, source, key, AddToTarget: false, LoopFuelPerIteration: LoopFuel + 2 + LiteralFuel);
            return true;
        }

        if (assignment.Value is BinaryExpression { Operator: "+" } add &&
            TryGetAccumulatingGet(add, assignment.Name, stackPlan, out source, out key))
        {
            plan = new LoopPlan(assignment.Name, source, key, AddToTarget: true, LoopFuelPerIteration: LoopFuel + 4 + LiteralFuel);
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
        var key = il.DeclareLocal(typeof(SandboxValue));
        var item = il.DeclareLocal(typeof(int));
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
        CompiledLiteralEmitter.EmitUncharged(il, plan.Key);
        il.Emit(OpCodes.Stloc, key);
        il.Emit(OpCodes.Ldloc, declare(plan.Source).Local);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.MapCountRaw)));
        il.Emit(OpCodes.Stloc, count);
        il.Emit(OpCodes.Ldloc, count);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.ListReadFuelRaw)));
        il.Emit(OpCodes.Stloc, readFuel);
        il.Emit(OpCodes.Ldloc, end);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, iterations);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, readFuel);
        il.Emit(OpCodes.Ldloc, iterations);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.CanBulkChargeFuel)));
        il.Emit(OpCodes.Brfalse, fallback);
        CompiledMeterEmitter.Fuel(il, 1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, key);
        il.Emit(OpCodes.Ldloc, iterations);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.CanBulkChargeSandboxValue)));
        il.Emit(OpCodes.Brfalse, fallback);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, readFuel);
        il.Emit(OpCodes.Ldloc, iterations);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.ChargeBulkFuel)));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, key);
        il.Emit(OpCodes.Ldloc, iterations);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.ChargeSandboxValues)));
        CompiledMeterEmitter.Fuel(il, 1);
        EmitMapGet(plan, key, item, il, declare);
        EmitLoopBody(fastLoop, finish, index, end, readFuel, key, item, range.LocalName, plan, il, declare, chargeReadFuel: false, chargeKey: false);

        il.MarkLabel(fallback);
        CompiledMeterEmitter.Fuel(il, 1);
        EmitMapGet(plan, key, item, il, declare);
        EmitLoopBody(fallbackLoop, finish, index, end, readFuel, key, item, range.LocalName, plan, il, declare, chargeReadFuel: true, chargeKey: true);
        il.MarkLabel(finish);
    }

    private static void EmitLoopBody(
        Label loop,
        Label finish,
        LocalBuilder index,
        LocalBuilder end,
        LocalBuilder readFuel,
        LocalBuilder key,
        LocalBuilder item,
        string loopLocal,
        LoopPlan plan,
        ILGenerator il,
        Func<string, (LocalBuilder Local, StackKind Kind)> declare,
        bool chargeReadFuel,
        bool chargeKey)
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
        if (chargeKey)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, key);
            il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.ChargeSandboxValue)));
        }

        if (plan.AddToTarget)
        {
            il.Emit(OpCodes.Ldloc, declare(plan.Target).Local);
            il.Emit(OpCodes.Ldloc, item);
            il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.AddI32Raw)));
        }
        else
        {
            il.Emit(OpCodes.Ldloc, item);
        }

        il.Emit(OpCodes.Stloc, declare(plan.Target).Local);
        il.Emit(OpCodes.Ldloc, index);
        EmitInt32(il, 1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, index);
        il.Emit(OpCodes.Br, loop);
    }

    private static void EmitMapGet(
        LoopPlan plan,
        LocalBuilder key,
        LocalBuilder item,
        ILGenerator il,
        Func<string, (LocalBuilder Local, StackKind Kind)> declare)
    {
        il.Emit(OpCodes.Ldloc, declare(plan.Source).Local);
        il.Emit(OpCodes.Ldloc, key);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.MapGetI32Raw)));
        il.Emit(OpCodes.Stloc, item);
    }

    private static bool TryGetAccumulatingGet(
        BinaryExpression expression,
        string target,
        LocalStackKindPlanner stackPlan,
        out string source,
        out SandboxValue key)
    {
        source = "";
        key = null!;
        return (IsTargetVariable(expression.Left, target) &&
                TryGetMapGetCall(expression.Right, stackPlan, out source, out key)) ||
               (IsTargetVariable(expression.Right, target) &&
                TryGetMapGetCall(expression.Left, stackPlan, out source, out key));
    }

    private static bool TryGetMapGetCall(
        Expression expression,
        LocalStackKindPlanner stackPlan,
        out string source,
        out SandboxValue key)
    {
        source = "";
        key = null!;
        if (expression is not CallExpression { Name: "map.get", Arguments.Count: 2 } call ||
            call.Arguments[0] is not VariableExpression variable ||
            call.Arguments[1] is not LiteralExpression literal ||
            stackPlan.LocalKind(variable.Name) != StackKind.Boxed)
        {
            return false;
        }

        source = variable.Name;
        key = literal.Value;
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
        SandboxValue Key,
        bool AddToTarget,
        int LoopFuelPerIteration);
}
