using System.Reflection.Emit;
using DotBoxD.Kernels.Bindings;

namespace DotBoxD.Kernels.Compiler.Emitters.Loops;

using static Compiler.IlEmitterPrimitives;

internal sealed class BranchedF64LoopFastPathEmitter
{
    private const int LoopFuel = 5;

    private readonly ILGenerator _il;
    private readonly LocalStackKindPlanner _stackPlan;
    private readonly ExpressionEmitter _expressions;
    private readonly IReadOnlyDictionary<string, SandboxFunction> _functions;
    private readonly IBindingCatalog _bindings;
    private readonly IReadOnlySet<string> _nonNegativeF64Locals;
    private readonly Func<string, (LocalBuilder Local, StackKind Kind)> _declare;

    private BranchedF64LoopFastPathEmitter(
        ILGenerator il,
        LocalStackKindPlanner stackPlan,
        ExpressionEmitter expressions,
        IReadOnlyDictionary<string, SandboxFunction> functions,
        IBindingCatalog bindings,
        IReadOnlySet<string> nonNegativeF64Locals,
        Func<string, (LocalBuilder Local, StackKind Kind)> declare)
    {
        _il = il;
        _stackPlan = stackPlan;
        _expressions = expressions;
        _functions = functions;
        _bindings = bindings;
        _nonNegativeF64Locals = nonNegativeF64Locals;
        _declare = declare;
    }

    public static bool TryEmit(
        ForRangeStatement range,
        ILGenerator il,
        LocalStackKindPlanner stackPlan,
        ExpressionEmitter expressions,
        IReadOnlyDictionary<string, SandboxFunction> functions,
        IBindingCatalog bindings,
        IReadOnlySet<string> nonNegativeF64Locals,
        Func<string, (LocalBuilder Local, StackKind Kind)> declare)
        => new BranchedF64LoopFastPathEmitter(
            il,
            stackPlan,
            expressions,
            functions,
            bindings,
            nonNegativeF64Locals,
            declare).TryEmit(range);

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
                _stackPlan.LocalKind(assignment.Name) != StackKind.F64 ||
                !RawF64ExpressionPlan.TryCreate(
                    assignment.Value,
                    assignment.Name,
                    _stackPlan,
                    _bindings,
                    _nonNegativeF64Locals,
                    out var expression) ||
                expression.BindingCallCount > 0)
            {
                return false;
            }

            assignments[i] = new AssignmentPlan(assignment.Name, expression);
            fuel += 1 + expression.FuelCost;
        }

        branch = new Branch(assignments, fuel);
        return true;
    }

    private readonly record struct AssignmentPlan(string Target, RawF64ExpressionPlan Expression);

    private readonly record struct Branch(AssignmentPlan[] Assignments, int Fuel);
}
