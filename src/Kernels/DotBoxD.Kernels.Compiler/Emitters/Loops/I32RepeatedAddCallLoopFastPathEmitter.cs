namespace DotBoxD.Kernels.Compiler.Emitters;

using System.Reflection.Emit;
using DotBoxD.Kernels;
using DotBoxD.Kernels.Runtime;
using static DotBoxD.Kernels.Compiler.IlEmitterPrimitives;

internal static class I32RepeatedAddCallLoopFastPathEmitter
{
    private const int LoopFuel = 5;

    public static bool TryEmit(
        ForRangeStatement range,
        ILGenerator il,
        LocalStackKindPlanner stackPlan,
        IReadOnlyDictionary<string, SandboxFunction> functions,
        Func<string, (LocalBuilder Local, StackKind Kind)> declare)
    {
        if (stackPlan.LocalKind(range.LocalName) != StackKind.I32 ||
            !CanEmitBound(range.Start, stackPlan) ||
            !CanEmitBound(range.End, stackPlan) ||
            !TryCreatePlan(range, stackPlan, functions, out var plan))
        {
            return false;
        }

        EmitLoop(range, il, declare, plan);
        return true;
    }

    private static bool TryCreatePlan(
        ForRangeStatement range,
        LocalStackKindPlanner stackPlan,
        IReadOnlyDictionary<string, SandboxFunction> functions,
        out LoopPlan plan)
    {
        plan = default;
        if (range.Body.Count != 1 ||
            range.Body[0] is not AssignmentStatement assignment ||
            stackPlan.LocalKind(assignment.Name) != StackKind.I32 ||
            assignment.Value is not CallExpression call ||
            !TryCreateCallPlan(call, assignment.Name, functions, out var callPlan))
        {
            return false;
        }

        plan = new LoopPlan(assignment.Name, callPlan);
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
        EmitBound(range.Start, il, declare);
        il.Emit(OpCodes.Stloc, index);
        EmitBound(range.End, il, declare);
        il.Emit(OpCodes.Stloc, end);

        var finish = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldloc, end);
        il.Emit(OpCodes.Bge, finish);

        il.Emit(OpCodes.Ldarg_0);
        EmitInt32(il, plan.Call.MaxInlineCallDepth);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.RequireAdditionalCallDepth)));

        il.Emit(OpCodes.Ldloc, end);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, iterations);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, declare(plan.Target).Local);
        il.Emit(OpCodes.Ldloc, iterations);
        EmitInt32(il, plan.Call.Delta);
        EmitInt32(il, LoopFuel + 1 + plan.Call.ExpressionFuelCost);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.AddRepeatedI32LoopRaw)));
        il.Emit(OpCodes.Stloc, declare(plan.Target).Local);

        il.Emit(OpCodes.Ldloc, end);
        EmitInt32(il, 1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, declare(range.LocalName).Local);
        il.MarkLabel(finish);
    }

    private static bool TryCreateCallPlan(
        CallExpression call,
        string target,
        IReadOnlyDictionary<string, SandboxFunction> functions,
        out CallPlan plan)
    {
        plan = default;
        if (call.Arguments.Count != 1 ||
            call.Arguments[0] is not VariableExpression argument ||
            !string.Equals(argument.Name, target, StringComparison.Ordinal) ||
            !functions.TryGetValue(call.Name, out var function) ||
            !TryGetDelta(function, out var delta, out var expression))
        {
            return false;
        }

        plan = new CallPlan(delta, ExpressionFuelCost(expression) + 4, MaxInlineCallDepth: 1);
        return true;
    }

    private static bool TryGetDelta(SandboxFunction function, out int delta, out Expression expression)
    {
        delta = 0;
        expression = null!;
        if (function.Parameters.Count != 1 ||
            function.Parameters[0].Type != SandboxType.I32 ||
            function.ReturnType != SandboxType.I32 ||
            function.Body.Count != 1 ||
            function.Body[0] is not ReturnStatement ret)
        {
            return false;
        }

        var parameter = function.Parameters[0].Name;
        if (ret.Value is VariableExpression variable &&
            string.Equals(variable.Name, parameter, StringComparison.Ordinal))
        {
            expression = ret.Value;
            return true;
        }

        if (TryGetAddDelta(ret.Value, parameter, out delta) ||
            TryGetSubtractDelta(ret.Value, parameter, out delta))
        {
            expression = ret.Value;
            return true;
        }

        return false;
    }

    private static bool TryGetAddDelta(Expression expression, string parameter, out int delta)
    {
        delta = 0;
        return expression is BinaryExpression { Operator: "+" } add &&
               ((IsParameter(add.Left, parameter) && TryReadI32(add.Right, out delta)) ||
                (IsParameter(add.Right, parameter) && TryReadI32(add.Left, out delta)));
    }

    private static bool TryGetSubtractDelta(Expression expression, string parameter, out int delta)
    {
        delta = 0;
        if (expression is not BinaryExpression { Operator: "-", Left: VariableExpression left } subtract ||
            !string.Equals(left.Name, parameter, StringComparison.Ordinal) ||
            !TryReadI32(subtract.Right, out var value) ||
            value == int.MinValue)
        {
            return false;
        }

        delta = -value;
        return true;
    }

    private static int ExpressionFuelCost(Expression expression)
        => expression switch {
            LiteralExpression { Value: I32Value } => 1,
            VariableExpression => 1,
            UnaryExpression { Operator: "-" } unary => 1 + ExpressionFuelCost(unary.Operand),
            BinaryExpression binary when binary.Operator is "+" or "-" or "*" or "/" or "%"
                => 1 + ExpressionFuelCost(binary.Left) + ExpressionFuelCost(binary.Right),
            _ => throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "unsupported i32 expression"))
        };

    private static bool IsParameter(Expression expression, string parameter)
        => expression is VariableExpression variable &&
           string.Equals(variable.Name, parameter, StringComparison.Ordinal);

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

    private readonly record struct LoopPlan(string Target, CallPlan Call);

    private readonly record struct CallPlan(int Delta, int ExpressionFuelCost, int MaxInlineCallDepth);
}
