using System.Reflection.Emit;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Compiler.Emitters.Loops;

using static Compiler.IlEmitterPrimitives;

internal sealed class I32ModuloBranchAccumulatorLoopFastPathEmitter
{
    private const int LoopFuel = 5;
    private const int ConditionFuel = 5;
    private const int BranchFuel = 4;

    private readonly ILGenerator _il;
    private readonly LocalStackKindPlanner _stackPlan;
    private readonly ExpressionEmitter _expressions;
    private readonly Func<string, (LocalBuilder Local, StackKind Kind)> _declare;

    private I32ModuloBranchAccumulatorLoopFastPathEmitter(
        ILGenerator il,
        LocalStackKindPlanner stackPlan,
        ExpressionEmitter expressions,
        Func<string, (LocalBuilder Local, StackKind Kind)> declare)
    {
        _il = il;
        _stackPlan = stackPlan;
        _expressions = expressions;
        _declare = declare;
    }

    public static bool TryEmit(
        ForRangeStatement range,
        ILGenerator il,
        LocalStackKindPlanner stackPlan,
        ExpressionEmitter expressions,
        Func<string, (LocalBuilder Local, StackKind Kind)> declare)
        => new I32ModuloBranchAccumulatorLoopFastPathEmitter(il, stackPlan, expressions, declare).TryEmit(range);

    private bool TryEmit(ForRangeStatement range)
    {
        if (!TryCreatePlan(range, out var plan))
        {
            return false;
        }

        var index = _il.DeclareLocal(typeof(int));
        var end = _il.DeclareLocal(typeof(int));
        var iterations = _il.DeclareLocal(typeof(int));
        _expressions.EmitAs(range.Start, StackKind.I32);
        _il.Emit(OpCodes.Stloc, index);
        _expressions.EmitAs(range.End, StackKind.I32);
        _il.Emit(OpCodes.Stloc, end);

        var fallback = _il.DefineLabel();
        var fallbackLoop = _il.DefineLabel();
        var finish = _il.DefineLabel();
        _il.Emit(OpCodes.Ldloc, index);
        _il.Emit(OpCodes.Ldloc, end);
        _il.Emit(OpCodes.Bge, finish);
        _il.Emit(OpCodes.Ldloc, end);
        _il.Emit(OpCodes.Ldloc, index);
        _il.Emit(OpCodes.Sub);
        _il.Emit(OpCodes.Stloc, iterations);

        EmitCanUse(plan, iterations);
        _il.Emit(OpCodes.Brfalse, fallback);
        EmitClosedForm(plan, iterations);
        _il.Emit(OpCodes.Ldloc, end);
        EmitInt32(_il, 1);
        _il.Emit(OpCodes.Sub);
        _il.Emit(OpCodes.Stloc, _declare(range.LocalName).Local);
        _il.Emit(OpCodes.Br, finish);

        _il.MarkLabel(fallback);
        EmitFallbackLoop(range.LocalName, plan, index, end, fallbackLoop, finish);
        _il.MarkLabel(finish);
        return true;
    }

    private void EmitCanUse(BranchPlan plan, LocalBuilder iterations)
    {
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, _declare(plan.Target).Local);
        _il.Emit(OpCodes.Ldloc, iterations);
        EmitInt32(_il, plan.Divisor);
        EmitInt32(_il, plan.Match);
        EmitInt32(_il, plan.ThenDelta);
        EmitInt32(_il, plan.ElseDelta);
        EmitInt32(_il, LoopFuel + 1 + ConditionFuel);
        EmitInt32(_il, BranchFuel);
        EmitInt32(_il, BranchFuel);
        _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.CanUseModuloBranchAccumulatorRaw)));
    }

    private void EmitClosedForm(BranchPlan plan, LocalBuilder iterations)
    {
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, _declare(plan.Target).Local);
        _il.Emit(OpCodes.Ldloc, iterations);
        EmitInt32(_il, plan.Divisor);
        EmitInt32(_il, plan.Match);
        EmitInt32(_il, plan.ThenDelta);
        EmitInt32(_il, plan.ElseDelta);
        EmitInt32(_il, LoopFuel + 1 + ConditionFuel);
        EmitInt32(_il, BranchFuel);
        EmitInt32(_il, BranchFuel);
        _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.AddModuloBranchDeltasI32LoopRaw)));
        _il.Emit(OpCodes.Stloc, _declare(plan.Target).Local);
    }

    private void EmitFallbackLoop(
        string loopLocal,
        BranchPlan plan,
        LocalBuilder index,
        LocalBuilder end,
        Label loop,
        Label finish)
    {
        var elseLabel = _il.DefineLabel();
        var nextLabel = _il.DefineLabel();
        _il.MarkLabel(loop);
        _il.Emit(OpCodes.Ldloc, index);
        _il.Emit(OpCodes.Ldloc, end);
        _il.Emit(OpCodes.Bge, finish);
        CompiledMeterEmitter.LoopIteration(_il, LoopFuel + 1 + ConditionFuel);
        _il.Emit(OpCodes.Ldloc, index);
        _il.Emit(OpCodes.Stloc, _declare(loopLocal).Local);
        EmitCondition(plan, index);
        _il.Emit(OpCodes.Brfalse, elseLabel);
        EmitDelta(plan.Target, plan.ThenDelta);
        _il.Emit(OpCodes.Br, nextLabel);
        _il.MarkLabel(elseLabel);
        EmitDelta(plan.Target, plan.ElseDelta);
        _il.MarkLabel(nextLabel);
        _il.Emit(OpCodes.Ldloc, index);
        EmitInt32(_il, 1);
        _il.Emit(OpCodes.Add);
        _il.Emit(OpCodes.Stloc, index);
        _il.Emit(OpCodes.Br, loop);
    }

    private void EmitCondition(BranchPlan plan, LocalBuilder index)
    {
        _il.Emit(OpCodes.Ldloc, index);
        EmitInt32(_il, plan.Divisor);
        _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.RemI32Raw)));
        EmitInt32(_il, plan.Match);
        _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.EqI32Raw)));
    }

    private void EmitDelta(string target, int delta)
    {
        CompiledMeterEmitter.Fuel(_il, BranchFuel);
        _il.Emit(OpCodes.Ldloc, _declare(target).Local);
        EmitInt32(_il, delta);
        _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.AddI32Raw)));
        _il.Emit(OpCodes.Stloc, _declare(target).Local);
    }

    private bool TryCreatePlan(ForRangeStatement range, out BranchPlan plan)
    {
        plan = default;
        if (_stackPlan.LocalKind(range.LocalName) != StackKind.I32 ||
            range.Start is not LiteralExpression { Value: I32Value { Value: 0 } } ||
            range.Body is not [IfStatement branch] ||
            !TryGetModuloCondition(branch.Condition, range.LocalName, out var divisor, out var match) ||
            !TryGetDelta(branch.Then, out var target, out var thenDelta) ||
            !TryGetDelta(branch.Else, out var elseTarget, out var elseDelta) ||
            !string.Equals(target, elseTarget, StringComparison.Ordinal) ||
            _stackPlan.LocalKind(target) != StackKind.I32 ||
            divisor <= 0)
        {
            return false;
        }

        plan = new BranchPlan(target, divisor, match, thenDelta, elseDelta);
        return true;
    }

    private static bool TryGetModuloCondition(Expression expression, string loopLocal, out int divisor, out int match)
    {
        divisor = 0;
        match = 0;
        return expression is BinaryExpression { Operator: "==" } equals &&
               (TryGetModuloEquals(equals.Left, equals.Right, loopLocal, out divisor, out match) ||
                TryGetModuloEquals(equals.Right, equals.Left, loopLocal, out divisor, out match));
    }

    private static bool TryGetModuloEquals(Expression rem, Expression literal, string loopLocal, out int divisor, out int match)
    {
        divisor = 0;
        match = 0;
        if (rem is not BinaryExpression
            {
                Operator: "%",
                Left: VariableExpression variable,
                Right: LiteralExpression { Value: I32Value divisorValue }
            } ||
            literal is not LiteralExpression { Value: I32Value matchValue } ||
            !string.Equals(variable.Name, loopLocal, StringComparison.Ordinal))
        {
            return false;
        }

        divisor = divisorValue.Value;
        match = matchValue.Value;
        return true;
    }

    private static bool TryGetDelta(IReadOnlyList<Statement> statements, out string target, out int delta)
    {
        target = "";
        delta = 0;
        return statements is [AssignmentStatement assignment] &&
               TryGetAddLiteral(assignment.Value, assignment.Name, out delta) &&
               (target = assignment.Name).Length > 0;
    }

    private static bool TryGetAddLiteral(Expression expression, string target, out int delta)
    {
        delta = 0;
        return expression is BinaryExpression { Operator: "+" } add &&
               ((IsTarget(add.Left, target) && TryGetLiteral(add.Right, out delta)) ||
                (IsTarget(add.Right, target) && TryGetLiteral(add.Left, out delta)));
    }

    private static bool IsTarget(Expression expression, string target)
        => expression is VariableExpression variable &&
           string.Equals(variable.Name, target, StringComparison.Ordinal);

    private static bool TryGetLiteral(Expression expression, out int value)
    {
        value = 0;
        if (expression is not LiteralExpression { Value: I32Value literal })
        {
            return false;
        }

        value = literal.Value;
        return true;
    }

    private readonly record struct BranchPlan(string Target, int Divisor, int Match, int ThenDelta, int ElseDelta);
}
