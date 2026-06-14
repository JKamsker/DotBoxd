namespace DotBoxD.Kernels.Compiler.Emitters;

using System.Reflection.Emit;
using DotBoxD.Kernels;
using DotBoxD.Kernels.Runtime;
using static DotBoxD.Kernels.Compiler.IlEmitterPrimitives;

internal static class I32ModuloIndexWhileLoopFastPathEmitter
{
    private const int LoopFuel = 5;
    private const int ConditionFuel = 3;
    private const int TotalAssignmentFuel = 6;
    private const int IndexAssignmentFuel = 4;

    public static bool TryEmit(
        WhileStatement loop,
        ILGenerator il,
        LocalStackKindPlanner stackPlan,
        Func<string, (LocalBuilder Local, StackKind Kind)> declare)
    {
        if (!TryCreatePlan(loop, stackPlan, out var plan))
        {
            return false;
        }

        EmitLoop(il, declare, plan);
        return true;
    }

    private static bool TryCreatePlan(
        WhileStatement loop,
        LocalStackKindPlanner stackPlan,
        out LoopPlan plan)
    {
        plan = default;
        if (!TryGetLessThanLocals(loop.Condition, stackPlan, out var indexName, out var endName) ||
            loop.Body.Count != 2 ||
            !TryGetModuloAssignment(loop.Body[0], indexName, stackPlan, out var targetName, out var divisor) ||
            !TryGetIncrement(loop.Body[1], indexName, stackPlan))
        {
            return false;
        }

        plan = new LoopPlan(
            targetName,
            indexName,
            endName,
            divisor,
            LoopFuel + ConditionFuel + TotalAssignmentFuel + IndexAssignmentFuel);
        return true;
    }

    private static void EmitLoop(
        ILGenerator il,
        Func<string, (LocalBuilder Local, StackKind Kind)> declare,
        LoopPlan plan)
    {
        var fallback = il.DefineLabel();
        var fallbackLoop = il.DefineLabel();
        var noIterations = il.DefineLabel();
        var finish = il.DefineLabel();
        var target = declare(plan.Target).Local;
        var index = declare(plan.Index).Local;
        var end = declare(plan.End).Local;

        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldloc, end);
        il.Emit(OpCodes.Bge, noIterations);

        il.Emit(OpCodes.Ldloc, target);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldloc, end);
        EmitInt32(il, plan.Divisor);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.CanUseModuloIndexAccumulatorRaw)));
        il.Emit(OpCodes.Brfalse, fallback);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, target);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldloc, end);
        EmitInt32(il, plan.Divisor);
        EmitInt32(il, plan.FuelPerIteration);
        EmitInt32(il, ConditionFuel);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.AddModuloIndexAccumulatorI32LoopRaw)));
        il.Emit(OpCodes.Stloc, target);
        il.Emit(OpCodes.Ldloc, end);
        il.Emit(OpCodes.Stloc, index);
        il.Emit(OpCodes.Br, finish);

        il.MarkLabel(noIterations);
        CompiledMeterEmitter.Fuel(il, ConditionFuel);
        il.Emit(OpCodes.Br, finish);

        il.MarkLabel(fallback);
        EmitFallbackLoop(fallbackLoop, finish, target, index, end, plan.Divisor, il);
        il.MarkLabel(finish);
    }

    private static void EmitFallbackLoop(
        Label loop,
        Label finish,
        LocalBuilder target,
        LocalBuilder index,
        LocalBuilder end,
        int divisor,
        ILGenerator il)
    {
        il.MarkLabel(loop);
        CompiledMeterEmitter.Fuel(il, ConditionFuel);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldloc, end);
        il.Emit(OpCodes.Bge, finish);
        CompiledMeterEmitter.LoopIteration(il, LoopFuel);

        CompiledMeterEmitter.Fuel(il, TotalAssignmentFuel);
        il.Emit(OpCodes.Ldloc, target);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.AddI32Raw)));
        EmitInt32(il, divisor);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.RemI32Raw)));
        il.Emit(OpCodes.Stloc, target);

        CompiledMeterEmitter.Fuel(il, IndexAssignmentFuel);
        il.Emit(OpCodes.Ldloc, index);
        EmitInt32(il, 1);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.AddI32Raw)));
        il.Emit(OpCodes.Stloc, index);
        il.Emit(OpCodes.Br, loop);
    }

    private static bool TryGetLessThanLocals(
        Expression expression,
        LocalStackKindPlanner stackPlan,
        out string leftName,
        out string rightName)
    {
        leftName = "";
        rightName = "";
        if (expression is BinaryExpression
            {
                Operator: "<",
                Left: VariableExpression left,
                Right: VariableExpression right
            } &&
            stackPlan.LocalKind(left.Name) == StackKind.I32 &&
            stackPlan.LocalKind(right.Name) == StackKind.I32)
        {
            leftName = left.Name;
            rightName = right.Name;
            return true;
        }

        return false;
    }

    private static bool TryGetModuloAssignment(
        Statement statement,
        string indexName,
        LocalStackKindPlanner stackPlan,
        out string targetName,
        out int divisor)
    {
        targetName = "";
        divisor = 0;
        if (statement is not AssignmentStatement assignment ||
            stackPlan.LocalKind(assignment.Name) != StackKind.I32 ||
            assignment.Value is not BinaryExpression
            {
                Operator: "%",
                Left: BinaryExpression { Operator: "+" } add,
                Right: LiteralExpression { Value: I32Value value }
            } ||
            value.Value <= 0 ||
            !TryGetTargetAndIndex(add, assignment.Name, indexName))
        {
            return false;
        }

        targetName = assignment.Name;
        divisor = value.Value;
        return true;
    }

    private static bool TryGetIncrement(
        Statement statement,
        string indexName,
        LocalStackKindPlanner stackPlan)
        => statement is AssignmentStatement assignment &&
           string.Equals(assignment.Name, indexName, StringComparison.Ordinal) &&
           stackPlan.LocalKind(indexName) == StackKind.I32 &&
           assignment.Value is BinaryExpression { Operator: "+" } add &&
           ((IsVariable(add.Left, indexName) && IsI32(add.Right, 1)) ||
            (IsVariable(add.Right, indexName) && IsI32(add.Left, 1)));

    private static bool TryGetTargetAndIndex(BinaryExpression add, string targetName, string indexName)
        => (IsVariable(add.Left, targetName) && IsVariable(add.Right, indexName)) ||
           (IsVariable(add.Right, targetName) && IsVariable(add.Left, indexName));

    private static bool IsVariable(Expression expression, string name)
        => expression is VariableExpression variable &&
           string.Equals(variable.Name, name, StringComparison.Ordinal);

    private static bool IsI32(Expression expression, int value)
        => expression is LiteralExpression { Value: I32Value i32 } && i32.Value == value;

    private readonly record struct LoopPlan(
        string Target,
        string Index,
        string End,
        int Divisor,
        int FuelPerIteration);
}
