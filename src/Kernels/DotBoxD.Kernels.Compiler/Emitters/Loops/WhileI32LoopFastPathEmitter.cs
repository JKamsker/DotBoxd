namespace DotBoxD.Kernels.Compiler.Emitters;

using System.Reflection.Emit;
using DotBoxD.Kernels;
using DotBoxD.Kernels.Runtime;
using static DotBoxD.Kernels.Compiler.IlEmitterPrimitives;

// Compiled fast path for `while (<i32 comparison>) { <i32 assigns> }`. Emits the condition and body as unboxed
// raw i32 and charges fuel in bulk: each condition evaluation (N iterations + 1 exit) charges the condition
// node fuel, and each executed iteration charges the loop base + straight-line body in one loop-iteration meter.
// Total per-iteration fuel is byte-identical to the general while emitter (condition nodes + LoopIteration 5 +
// per-statement 1 + per-node body fuel); the while statement's own Fuel(1) is charged by EmitStatement upstream.
internal sealed class WhileI32LoopFastPathEmitter
{
    private const int LoopFuel = 5;

    private readonly ILGenerator _il;
    private readonly LocalStackKindPlanner _stackPlan;
    private readonly ExpressionEmitter _expressions;
    private readonly IReadOnlyDictionary<string, SandboxFunction> _functions;
    private readonly Func<string, (LocalBuilder Local, StackKind Kind)> _declare;

    private WhileI32LoopFastPathEmitter(ILGenerator il, LocalStackKindPlanner stackPlan, ExpressionEmitter expressions, IReadOnlyDictionary<string, SandboxFunction> functions, Func<string, (LocalBuilder Local, StackKind Kind)> declare)
    {
        _il = il;
        _stackPlan = stackPlan;
        _expressions = expressions;
        _functions = functions;
        _declare = declare;
    }

    public static bool TryEmit(WhileStatement loop, ILGenerator il, LocalStackKindPlanner stackPlan, ExpressionEmitter expressions, IReadOnlyDictionary<string, SandboxFunction> functions, Func<string, (LocalBuilder Local, StackKind Kind)> declare)
        => new WhileI32LoopFastPathEmitter(il, stackPlan, expressions, functions, declare).TryEmit(loop);

    private bool TryEmit(WhileStatement loop)
    {
        if (loop.Body.Count == 0 ||
            !TryCreateCondition(loop.Condition, out var condition) ||
            !TryCreateBody(loop.Body, out var body, out var bodyFuel))
        {
            return false;
        }

        var startLabel = _il.DefineLabel();
        var finishLabel = _il.DefineLabel();

        _il.MarkLabel(startLabel);
        CompiledMeterEmitter.Fuel(_il, condition.Fuel);
        condition.Left.Emit(_il, _declare);
        condition.Right.Emit(_il, _declare);
        _il.Emit(OpCodes.Call, Runtime(condition.Method));
        _il.Emit(OpCodes.Brfalse, finishLabel);

        CompiledMeterEmitter.LoopIteration(_il, LoopFuel + bodyFuel);
        for (var i = 0; i < body.Length; i++)
        {
            var assignment = body[i];
            assignment.Expression.Emit(_il, _declare);
            _il.Emit(OpCodes.Stloc, _declare(assignment.Target).Local);
        }

        _il.Emit(OpCodes.Br, startLabel);
        _il.MarkLabel(finishLabel);
        return true;
    }

    private bool TryCreateCondition(Expression expression, out Condition condition)
    {
        condition = default;
        if (expression is not BinaryExpression { Operator: "==" or "!=" or "<" or "<=" or ">" or ">=" } binary ||
            !RawI32ExpressionPlan.TryCreate(binary.Left, _stackPlan, _functions, out var left) ||
            !RawI32ExpressionPlan.TryCreate(binary.Right, _stackPlan, _functions, out var right))
        {
            return false;
        }

        var method = binary.Operator switch
        {
            "<" => nameof(CompiledRuntime.LtI32Raw),
            "<=" => nameof(CompiledRuntime.LteI32Raw),
            ">" => nameof(CompiledRuntime.GtI32Raw),
            ">=" => nameof(CompiledRuntime.GteI32Raw),
            "==" => nameof(CompiledRuntime.EqI32Raw),
            _ => nameof(CompiledRuntime.NeI32Raw)
        };
        condition = new Condition(left, right, method, 1 + left.FuelCost + right.FuelCost);
        return true;
    }

    private bool TryCreateBody(IReadOnlyList<Statement> statements, out AssignmentPlan[] body, out int bodyFuel)
    {
        body = [];
        bodyFuel = 0;
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

        body = assignments;
        bodyFuel = fuel;
        return true;
    }

    private readonly record struct AssignmentPlan(string Target, RawI32ExpressionPlan Expression);

    private readonly record struct Condition(RawI32ExpressionPlan Left, RawI32ExpressionPlan Right, string Method, int Fuel);
}
