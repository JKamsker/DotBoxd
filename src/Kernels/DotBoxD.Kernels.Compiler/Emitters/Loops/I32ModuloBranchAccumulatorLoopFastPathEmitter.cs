namespace DotBoxD.Kernels.Compiler.Emitters;

using System.Reflection.Emit;
using DotBoxD.Kernels;
using DotBoxD.Kernels.Runtime;
using static DotBoxD.Kernels.Compiler.IlEmitterPrimitives;

internal static class I32ModuloBranchAccumulatorLoopFastPathEmitter
{
    private const int LoopFuel = 5;
    private const int IfStatementFuel = 1;
    private const int AssignmentFuel = 1;
    private const int BranchExpressionFuel = 3;
    private const int ModuloEqualsConditionFuel = 5;

    public static bool TryEmit(
        ForRangeStatement range,
        ILGenerator il,
        LocalStackKindPlanner stackPlan,
        Func<string, (LocalBuilder Local, StackKind Kind)> declare)
    {
        if (stackPlan.LocalKind(range.LocalName) != StackKind.I32 ||
            !IsZeroStart(range.Start) ||
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
            range.Body[0] is not IfStatement branch ||
            !TryGetModuloEquals(branch.Condition, range.LocalName, out var divisor, out var matchRemainder) ||
            !TryGetBranchAssignment(branch.Then, stackPlan, out var target, out var thenDelta) ||
            !TryGetBranchAssignment(branch.Else, stackPlan, out var elseTarget, out var elseDelta) ||
            !string.Equals(target, elseTarget, StringComparison.Ordinal) ||
            !DeltasHaveSameDirection(thenDelta, elseDelta))
        {
            return false;
        }

        plan = new LoopPlan(
            target,
            divisor,
            matchRemainder,
            thenDelta,
            elseDelta,
            LoopFuel + IfStatementFuel + ModuloEqualsConditionFuel + AssignmentFuel + BranchExpressionFuel);
        return true;
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
        EmitInt32(il, 0);
        il.Emit(OpCodes.Stloc, index);
        EmitBound(range.End, il, declare);
        il.Emit(OpCodes.Stloc, end);

        var finish = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldloc, end);
        il.Emit(OpCodes.Bge, finish);

        il.Emit(OpCodes.Ldloc, end);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, iterations);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, declare(plan.Target).Local);
        il.Emit(OpCodes.Ldloc, iterations);
        EmitInt32(il, plan.Divisor);
        EmitInt32(il, plan.MatchRemainder);
        EmitInt32(il, plan.ThenDelta);
        EmitInt32(il, plan.ElseDelta);
        EmitInt32(il, plan.FuelPerIteration);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.AddModuloBranchDeltasI32LoopRaw)));
        il.Emit(OpCodes.Stloc, declare(plan.Target).Local);

        il.Emit(OpCodes.Ldloc, end);
        EmitInt32(il, 1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, declare(range.LocalName).Local);
        il.MarkLabel(finish);
    }

    private static bool TryGetModuloEquals(
        Expression expression,
        string loopLocal,
        out int divisor,
        out int matchRemainder)
    {
        divisor = 0;
        matchRemainder = 0;
        if (expression is not BinaryExpression { Operator: "==" } equals)
        {
            return false;
        }

        return (TryGetLoopModulo(equals.Left, loopLocal, out divisor) &&
                TryReadI32(equals.Right, out matchRemainder)) ||
               (TryGetLoopModulo(equals.Right, loopLocal, out divisor) &&
                TryReadI32(equals.Left, out matchRemainder));
    }

    private static bool TryGetLoopModulo(Expression expression, string loopLocal, out int divisor)
    {
        divisor = 0;
        if (expression is BinaryExpression
            {
                Operator: "%",
                Left: VariableExpression variable,
                Right: LiteralExpression { Value: I32Value value }
            } &&
            value.Value > 0 &&
            string.Equals(variable.Name, loopLocal, StringComparison.Ordinal))
        {
            divisor = value.Value;
            return true;
        }

        return false;
    }

    private static bool TryGetBranchAssignment(
        IReadOnlyList<Statement> branch,
        LocalStackKindPlanner stackPlan,
        out string target,
        out int delta)
    {
        target = "";
        delta = 0;
        if (branch.Count != 1 ||
            branch[0] is not AssignmentStatement assignment ||
            stackPlan.LocalKind(assignment.Name) != StackKind.I32 ||
            !TryGetDelta(assignment.Value, assignment.Name, out delta))
        {
            return false;
        }

        target = assignment.Name;
        return true;
    }

    private static bool TryGetDelta(Expression expression, string target, out int delta)
    {
        delta = 0;
        return expression is BinaryExpression { Operator: "+" } add &&
               ((IsTarget(add.Left, target) && TryReadI32(add.Right, out delta)) ||
                (IsTarget(add.Right, target) && TryReadI32(add.Left, out delta)));
    }

    private static bool TryReadI32(Expression expression, out int value)
    {
        if (expression is LiteralExpression { Value: I32Value i32 })
        {
            value = i32.Value;
            return true;
        }

        value = 0;
        return false;
    }

    private static bool IsTarget(Expression expression, string target)
        => expression is VariableExpression variable &&
           string.Equals(variable.Name, target, StringComparison.Ordinal);

    private static bool IsZeroStart(Expression expression)
        => expression is LiteralExpression { Value: I32Value { Value: 0 } };

    private static bool DeltasHaveSameDirection(int left, int right)
        => left >= 0 && right >= 0 || left <= 0 && right <= 0;

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
        int Divisor,
        int MatchRemainder,
        int ThenDelta,
        int ElseDelta,
        int FuelPerIteration);
}
