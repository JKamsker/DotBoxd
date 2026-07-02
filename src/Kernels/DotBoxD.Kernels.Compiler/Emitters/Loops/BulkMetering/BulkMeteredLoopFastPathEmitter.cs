using System.Reflection.Emit;

namespace DotBoxD.Kernels.Compiler.Emitters.Loops;

using static Compiler.IlEmitterPrimitives;

internal sealed class BulkMeteredLoopFastPathEmitter
{
    private const int LoopFuel = 5;

    private readonly ILGenerator _il;
    private readonly ExpressionEmitter _meteredExpressions;
    private readonly ExpressionEmitter _bulkExpressions;
    private readonly BulkMeteredLoopPlanner _planner;
    private readonly Func<string, (LocalBuilder Local, StackKind Kind)> _declare;

    private BulkMeteredLoopFastPathEmitter(
        ILGenerator il,
        LocalStackKindPlanner stackPlan,
        ExpressionEmitter expressions,
        Func<string, (LocalBuilder Local, StackKind Kind)> declare)
    {
        _il = il;
        _meteredExpressions = expressions;
        _bulkExpressions = expressions.WithoutExpressionFuel();
        _planner = new BulkMeteredLoopPlanner(stackPlan);
        _declare = declare;
    }

    public static bool TryEmit(
        ForRangeStatement range,
        ILGenerator il,
        LocalStackKindPlanner stackPlan,
        ExpressionEmitter expressions,
        Func<string, (LocalBuilder Local, StackKind Kind)> declare)
        => new BulkMeteredLoopFastPathEmitter(il, stackPlan, expressions, declare).TryEmit(range);

    public static bool TryEmit(
        WhileStatement loop,
        ILGenerator il,
        LocalStackKindPlanner stackPlan,
        ExpressionEmitter expressions,
        Func<string, (LocalBuilder Local, StackKind Kind)> declare)
        => new BulkMeteredLoopFastPathEmitter(il, stackPlan, expressions, declare).TryEmit(loop);

    private bool TryEmit(ForRangeStatement range)
    {
        if (!_planner.HasI32Local(range.LocalName) ||
            !_planner.TryCreateBlock(range.Body, out var body))
        {
            return false;
        }

        var index = _il.DeclareLocal(typeof(int));
        var end = _il.DeclareLocal(typeof(int));
        _meteredExpressions.EmitAs(range.Start, StackKind.I32);
        _il.Emit(OpCodes.Stloc, index);
        _meteredExpressions.EmitAs(range.End, StackKind.I32);
        _il.Emit(OpCodes.Stloc, end);

        var startLabel = _il.DefineLabel();
        var finishLabel = _il.DefineLabel();
        _il.MarkLabel(startLabel);
        _il.Emit(OpCodes.Ldloc, index);
        _il.Emit(OpCodes.Ldloc, end);
        _il.Emit(OpCodes.Bge, finishLabel);
        CompiledMeterEmitter.LoopIteration(_il, LoopFuel + body.AlwaysFuel);
        _il.Emit(OpCodes.Ldloc, index);
        _il.Emit(OpCodes.Stloc, _declare(range.LocalName).Local);
        EmitBlock(body);
        _il.Emit(OpCodes.Ldloc, index);
        EmitInt32(_il, 1);
        _il.Emit(OpCodes.Add);
        _il.Emit(OpCodes.Stloc, index);
        _il.Emit(OpCodes.Br, startLabel);
        _il.MarkLabel(finishLabel);
        return true;
    }

    private bool TryEmit(WhileStatement loop)
    {
        if (!_planner.TryMeasureExpression(loop.Condition, StackKind.Bool, out var conditionFuel) ||
            !_planner.TryCreateBlock(loop.Body, out var body))
        {
            return false;
        }

        var startLabel = _il.DefineLabel();
        var finishLabel = _il.DefineLabel();
        _il.MarkLabel(startLabel);
        CompiledMeterEmitter.Fuel(_il, conditionFuel);
        _bulkExpressions.EmitAs(loop.Condition, StackKind.Bool);
        _il.Emit(OpCodes.Brfalse, finishLabel);
        CompiledMeterEmitter.LoopIteration(_il, LoopFuel + body.AlwaysFuel);
        EmitBlock(body);
        _il.Emit(OpCodes.Br, startLabel);
        _il.MarkLabel(finishLabel);
        return true;
    }

    private void EmitBlock(BulkMeteredBlockPlan block)
    {
        for (var i = 0; i < block.Statements.Length; i++)
        {
            EmitStatement(block.Statements[i]);
        }
    }

    private void EmitStatement(BulkMeteredStatementPlan statement)
    {
        if (!statement.IsBranch)
        {
            EmitAssignment(statement.Assignment);
            return;
        }

        var elseLabel = _il.DefineLabel();
        var nextLabel = _il.DefineLabel();
        _bulkExpressions.EmitAs(statement.Condition!, StackKind.Bool);
        _il.Emit(OpCodes.Brfalse, elseLabel);
        EmitBranch(statement.Then);
        _il.Emit(OpCodes.Br, nextLabel);
        _il.MarkLabel(elseLabel);
        EmitBranch(statement.Else);
        _il.MarkLabel(nextLabel);
    }

    private void EmitBranch(BulkMeteredBranchPlan branch)
    {
        if (branch.Fuel > 0)
        {
            CompiledMeterEmitter.Fuel(_il, branch.Fuel);
        }

        for (var i = 0; i < branch.Assignments.Length; i++)
        {
            EmitAssignment(branch.Assignments[i]);
        }
    }

    private void EmitAssignment(BulkMeteredAssignmentPlan assignment)
    {
        _bulkExpressions.EmitAs(assignment.Value, assignment.Kind);
        _il.Emit(OpCodes.Stloc, _declare(assignment.Target).Local);
    }
}
