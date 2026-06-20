using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Compiler.Emitters.Loops;

namespace DotBoxD.Kernels.Compiler.Emitters;

using System.Reflection.Emit;
using DotBoxD.Kernels;
using static DotBoxD.Kernels.Compiler.IlEmitterPrimitives;

internal sealed class I32LoopFastPathEmitter
{
    private const int LoopFuel = 5;
    private const int MaxEstimatedInstructionsBetweenMeters = 30;

    private readonly ILGenerator _il;
    private readonly LocalStackKindPlanner _stackPlan;
    private readonly ExpressionEmitter _expressions;
    private readonly IReadOnlyDictionary<string, SandboxFunction> _functions;
    private readonly IBindingCatalog _bindings;
    private readonly Func<string, (LocalBuilder Local, StackKind Kind)> _declare;

    private I32LoopFastPathEmitter(
        ILGenerator il,
        LocalStackKindPlanner stackPlan,
        ExpressionEmitter expressions,
        IReadOnlyDictionary<string, SandboxFunction> functions,
        IBindingCatalog bindings,
        Func<string, (LocalBuilder Local, StackKind Kind)> declare)
    {
        _il = il;
        _stackPlan = stackPlan;
        _expressions = expressions;
        _functions = functions;
        _bindings = bindings;
        _declare = declare;
    }

    public static bool TryEmit(
        ForRangeStatement range,
        ILGenerator il,
        LocalStackKindPlanner stackPlan,
        ExpressionEmitter expressions,
        IReadOnlyDictionary<string, SandboxFunction> functions,
        IBindingCatalog bindings,
        Func<string, (LocalBuilder Local, StackKind Kind)> declare)
        => new I32LoopFastPathEmitter(il, stackPlan, expressions, functions, bindings, declare).TryEmit(range);

    private bool TryEmit(ForRangeStatement range)
    {
        if (_stackPlan.LocalKind(range.LocalName) != StackKind.I32 ||
            !TryCreateBodyPlan(range, out var body, out var fuelPerIteration, out var bodyInstructionCost) ||
            EstimatedLoopInstructionCost(bodyInstructionCost) > MaxEstimatedInstructionsBetweenMeters)
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

    private bool TryCreateBodyPlan(
        ForRangeStatement range,
        out AssignmentPlan[] body,
        out int fuelPerIteration,
        out int instructionCost)
    {
        body = new AssignmentPlan[range.Body.Count];
        fuelPerIteration = LoopFuel;
        instructionCost = 0;

        for (var i = 0; i < range.Body.Count; i++)
        {
            if (range.Body[i] is not AssignmentStatement assignment ||
                _stackPlan.LocalKind(assignment.Name) != StackKind.I32 ||
                !RawI32ExpressionPlan.TryCreate(assignment.Value, _stackPlan, _functions, _bindings, out var expression))
            {
                body = [];
                return false;
            }

            body[i] = new AssignmentPlan(assignment.Name, expression);
            fuelPerIteration += 1 + expression.FuelCost;
            instructionCost += 1 + expression.InstructionCost;
        }

        return true;
    }

    private static int EstimatedLoopInstructionCost(int bodyInstructionCost)
        // loop local store, body, index increment, back edge, condition, and next meter arguments.
        => 2 + bodyInstructionCost + 4 + 1 + 3 + 2;

    private readonly record struct AssignmentPlan(string Target, RawI32ExpressionPlan Expression);
}
