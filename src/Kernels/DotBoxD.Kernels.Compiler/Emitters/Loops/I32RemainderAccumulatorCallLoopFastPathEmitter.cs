namespace DotBoxD.Kernels.Compiler.Emitters;

using System.Reflection.Emit;
using DotBoxD.Kernels;
using DotBoxD.Kernels.Runtime;
using static DotBoxD.Kernels.Compiler.IlEmitterPrimitives;

internal static class I32RemainderAccumulatorCallLoopFastPathEmitter
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
            !IsZeroStart(range.Start) ||
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
            !TryCreateCallPlan(call, assignment.Name, range.LocalName, stackPlan, functions, out var callPlan))
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
        EmitInt32(il, 0);
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
        EmitInt32(il, plan.Call.Divisor);
        EmitInt32(il, LoopFuel + 1 + plan.Call.ExpressionFuelCost);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.AddRemainderCycleI32LoopRaw)));
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
        string loopLocal,
        LocalStackKindPlanner stackPlan,
        IReadOnlyDictionary<string, SandboxFunction> functions,
        out CallPlan plan)
    {
        plan = default;
        if (call.Arguments.Count != 2 ||
            !TryGetTargetAndModulo(call, target, loopLocal, stackPlan, out var divisor, out var argumentFuel) ||
            !functions.TryGetValue(call.Name, out var function) ||
            !IsTwoArgumentAdd(function, out var bodyFuel))
        {
            return false;
        }

        plan = new CallPlan(divisor, argumentFuel + bodyFuel + 3, MaxInlineCallDepth: 1);
        return true;
    }

    private static bool TryGetTargetAndModulo(
        CallExpression call,
        string target,
        string loopLocal,
        LocalStackKindPlanner stackPlan,
        out int divisor,
        out int argumentFuel)
    {
        if (IsTarget(call.Arguments[0], target) &&
            TryGetLoopModulo(call.Arguments[1], loopLocal, stackPlan, out divisor, out var moduloFuel))
        {
            argumentFuel = 1 + moduloFuel;
            return true;
        }

        if (IsTarget(call.Arguments[1], target) &&
            TryGetLoopModulo(call.Arguments[0], loopLocal, stackPlan, out divisor, out moduloFuel))
        {
            argumentFuel = 1 + moduloFuel;
            return true;
        }

        divisor = 0;
        argumentFuel = 0;
        return false;
    }

    private static bool TryGetLoopModulo(
        Expression expression,
        string loopLocal,
        LocalStackKindPlanner stackPlan,
        out int divisor,
        out int fuelCost)
    {
        if (I32IndexExpressionPlan.TryCreate(expression, stackPlan, out var plan) &&
            plan.TryGetVariableRemainderConstant(out var variable, out divisor) &&
            divisor > 0 &&
            string.Equals(variable, loopLocal, StringComparison.Ordinal))
        {
            fuelCost = plan.FuelCost;
            return true;
        }

        divisor = 0;
        fuelCost = 0;
        return false;
    }

    private static bool IsTwoArgumentAdd(SandboxFunction function, out int bodyFuel)
    {
        bodyFuel = 0;
        if (function.Parameters.Count != 2 ||
            function.Parameters.Any(parameter => parameter.Type != SandboxType.I32) ||
            function.ReturnType != SandboxType.I32 ||
            function.Body.Count != 1 ||
            function.Body[0] is not ReturnStatement { Value: BinaryExpression { Operator: "+" } add } ||
            !IsParameterPair(add, function.Parameters[0].Name, function.Parameters[1].Name))
        {
            return false;
        }

        bodyFuel = 3;
        return true;
    }

    private static bool IsTarget(Expression expression, string target)
        => expression is VariableExpression variable &&
           string.Equals(variable.Name, target, StringComparison.Ordinal);

    private static bool IsParameterPair(BinaryExpression add, string leftParameter, string rightParameter)
        => (IsParameter(add.Left, leftParameter) && IsParameter(add.Right, rightParameter)) ||
           (IsParameter(add.Left, rightParameter) && IsParameter(add.Right, leftParameter));

    private static bool IsParameter(Expression expression, string parameter)
        => expression is VariableExpression variable &&
           string.Equals(variable.Name, parameter, StringComparison.Ordinal);

    private static bool IsZeroStart(Expression expression)
        => expression is LiteralExpression { Value: I32Value { Value: 0 } };

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

    private readonly record struct CallPlan(int Divisor, int ExpressionFuelCost, int MaxInlineCallDepth);
}
