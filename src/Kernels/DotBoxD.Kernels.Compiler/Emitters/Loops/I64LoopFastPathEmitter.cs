namespace DotBoxD.Kernels.Compiler.Emitters;

using System.Reflection.Emit;
using DotBoxD.Kernels;
using static DotBoxD.Kernels.Compiler.IlEmitterPrimitives;

// Compiled fast path for `forRange { <i64 assigns> }` (i32 loop variable, i64 assignment targets). Emits the
// body as unboxed raw i64 (RawI64ExpressionPlan) with one bulk loop-iteration meter, matching the general
// emitter's total per-iteration fuel (loop base 5 + per assignment 1 + expression node fuel).
internal sealed class I64LoopFastPathEmitter
{
    private const int LoopFuel = 5;

    private readonly ILGenerator _il;
    private readonly LocalStackKindPlanner _stackPlan;
    private readonly ExpressionEmitter _expressions;
    private readonly Func<string, (LocalBuilder Local, StackKind Kind)> _declare;

    private I64LoopFastPathEmitter(ILGenerator il, LocalStackKindPlanner stackPlan, ExpressionEmitter expressions, Func<string, (LocalBuilder Local, StackKind Kind)> declare)
    {
        _il = il;
        _stackPlan = stackPlan;
        _expressions = expressions;
        _declare = declare;
    }

    public static bool TryEmit(ForRangeStatement range, ILGenerator il, LocalStackKindPlanner stackPlan, ExpressionEmitter expressions, Func<string, (LocalBuilder Local, StackKind Kind)> declare)
        => new I64LoopFastPathEmitter(il, stackPlan, expressions, declare).TryEmit(range);

    private bool TryEmit(ForRangeStatement range)
    {
        if (_stackPlan.LocalKind(range.LocalName) != StackKind.I32 ||
            !TryCreateBody(range, out var body, out var fuelPerIteration))
        {
            return false;
        }

        var index = _il.DeclareLocal(typeof(int));
        var end = _il.DeclareLocal(typeof(int));
        _expressions.EmitAs(range.Start, StackKind.I32);
        _il.Emit(OpCodes.Stloc, index);
        _expressions.EmitAs(range.End, StackKind.I32);
        _il.Emit(OpCodes.Stloc, end);

        var startLabel = _il.DefineLabel();
        var finishLabel = _il.DefineLabel();
        _il.MarkLabel(startLabel);
        _il.Emit(OpCodes.Ldloc, index);
        _il.Emit(OpCodes.Ldloc, end);
        _il.Emit(OpCodes.Bge, finishLabel);
        CompiledMeterEmitter.LoopIteration(_il, fuelPerIteration);

        var (loopVar, _) = _declare(range.LocalName);
        _il.Emit(OpCodes.Ldloc, index);
        _il.Emit(OpCodes.Stloc, loopVar);

        for (var i = 0; i < body.Length; i++)
        {
            var assignment = body[i];
            assignment.Expression.Emit(_il, _declare);
            _il.Emit(OpCodes.Stloc, _declare(assignment.Target).Local);
        }

        _il.Emit(OpCodes.Ldloc, index);
        EmitInt32(_il, 1);
        _il.Emit(OpCodes.Add);
        _il.Emit(OpCodes.Stloc, index);
        _il.Emit(OpCodes.Br, startLabel);
        _il.MarkLabel(finishLabel);
        return true;
    }

    private bool TryCreateBody(ForRangeStatement range, out AssignmentPlan[] body, out int fuelPerIteration)
    {
        body = [];
        fuelPerIteration = LoopFuel;
        if (range.Body.Count == 0)
        {
            return false;
        }

        var assignments = new AssignmentPlan[range.Body.Count];
        var fuel = LoopFuel;
        for (var i = 0; i < range.Body.Count; i++)
        {
            if (range.Body[i] is not AssignmentStatement assignment ||
                _stackPlan.LocalKind(assignment.Name) != StackKind.I64 ||
                !RawI64ExpressionPlan.TryCreate(assignment.Value, _stackPlan, out var expression))
            {
                return false;
            }

            assignments[i] = new AssignmentPlan(assignment.Name, expression);
            fuel += 1 + expression.FuelCost;
        }

        body = assignments;
        fuelPerIteration = fuel;
        return true;
    }

    private readonly record struct AssignmentPlan(string Target, RawI64ExpressionPlan Expression);
}
