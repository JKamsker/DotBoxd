using System.Reflection.Emit;

namespace DotBoxD.Kernels.Compiler.Emitters.Loops;

using static Compiler.IlEmitterPrimitives;

// Compiled fast path for `forRange { if (<i32 comparison>) { <i32 assigns> } else { <i32 assigns> } }`.
// Emits the condition and both branches as unboxed raw i32 (via RawI32ExpressionPlan + the *I32Raw helpers) and
// charges fuel in bulk per iteration instead of per node: the loop iteration meter covers the loop base + the
// if-statement + the (always-evaluated) condition, and each branch charges its own statically-known body fuel
// once. Total per-iteration fuel is byte-identical to the general emitter (LoopIteration 5 + Fuel(1) per
// statement + Fuel(1) per expression node).
internal sealed class BranchedI32LoopFastPathEmitter
{
    private const int LoopFuel = 5;

    private readonly ILGenerator _il;
    private readonly LocalStackKindPlanner _stackPlan;
    private readonly ExpressionEmitter _expressions;
    private readonly IReadOnlyDictionary<string, SandboxFunction> _functions;
    private readonly Func<string, (LocalBuilder Local, StackKind Kind)> _declare;

    private BranchedI32LoopFastPathEmitter(ILGenerator il, LocalStackKindPlanner stackPlan, ExpressionEmitter expressions, IReadOnlyDictionary<string, SandboxFunction> functions, Func<string, (LocalBuilder Local, StackKind Kind)> declare)
    {
        _il = il;
        _stackPlan = stackPlan;
        _expressions = expressions;
        _functions = functions;
        _declare = declare;
    }

    public static bool TryEmit(ForRangeStatement range, ILGenerator il, LocalStackKindPlanner stackPlan, ExpressionEmitter expressions, IReadOnlyDictionary<string, SandboxFunction> functions, Func<string, (LocalBuilder Local, StackKind Kind)> declare)
        => new BranchedI32LoopFastPathEmitter(il, stackPlan, expressions, functions, declare).TryEmit(range);

    private bool TryEmit(ForRangeStatement range)
    {
        if (_stackPlan.LocalKind(range.LocalName) != StackKind.I32 ||
            range.Body.Count != 1 ||
            range.Body[0] is not IfStatement branch ||
            !RawI32ConditionPlan.TryCreate(branch.Condition, _stackPlan, _functions, out var condition) ||
            !TryCreateBranch(branch.Then, out var thenBranch) ||
            !TryCreateBranch(branch.Else, out var elseBranch))
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
        var elseLabel = _il.DefineLabel();
        var nextLabel = _il.DefineLabel();
        var finishLabel = _il.DefineLabel();

        _il.MarkLabel(startLabel);
        _il.Emit(OpCodes.Ldloc, index);
        _il.Emit(OpCodes.Ldloc, end);
        _il.Emit(OpCodes.Bge, finishLabel);

        // Loop base + the if-statement + the always-evaluated condition, charged once for the iteration.
        CompiledMeterEmitter.LoopIteration(_il, LoopFuel + 1 + condition.Fuel);
        var (loopVar, _) = _declare(range.LocalName);
        _il.Emit(OpCodes.Ldloc, index);
        _il.Emit(OpCodes.Stloc, loopVar);

        condition.Emit(_il, _declare);
        _il.Emit(OpCodes.Brfalse, elseLabel);

        EmitBranch(thenBranch);
        _il.Emit(OpCodes.Br, nextLabel);
        _il.MarkLabel(elseLabel);
        EmitBranch(elseBranch);
        _il.MarkLabel(nextLabel);

        _il.Emit(OpCodes.Ldloc, index);
        EmitInt32(_il, 1);
        _il.Emit(OpCodes.Add);
        _il.Emit(OpCodes.Stloc, index);
        _il.Emit(OpCodes.Br, startLabel);
        _il.MarkLabel(finishLabel);
        return true;
    }

    private void EmitBranch(Branch branch)
    {
        if (branch.Fuel > 0)
        {
            CompiledMeterEmitter.Fuel(_il, branch.Fuel);
        }

        for (var i = 0; i < branch.Assignments.Length; i++)
        {
            var assignment = branch.Assignments[i];
            assignment.Expression.Emit(_il, _declare);
            _il.Emit(OpCodes.Stloc, _declare(assignment.Target).Local);
        }
    }

    private bool TryCreateBranch(IReadOnlyList<Statement> statements, out Branch branch)
    {
        branch = default;
        var assignments = new AssignmentPlan[statements.Count];
        var fuel = 0;
        for (var i = 0; i < statements.Count; i++)
        {
            if (statements[i] is not AssignmentStatement assignment ||
                _stackPlan.LocalKind(assignment.Name) != StackKind.I32 ||
                !RawI32ExpressionPlan.TryCreate(assignment.Value, _stackPlan, _functions, out var expression))
            {
                return false;
            }

            assignments[i] = new AssignmentPlan(assignment.Name, expression);
            fuel += 1 + expression.FuelCost;
        }

        branch = new Branch(assignments, fuel);
        return true;
    }

    private readonly record struct AssignmentPlan(string Target, RawI32ExpressionPlan Expression);

    private readonly record struct Branch(AssignmentPlan[] Assignments, int Fuel);
}
