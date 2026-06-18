using System.Reflection.Emit;
using DotBoxD.Kernels.Runtime;

namespace DotBoxD.Kernels.Compiler.Emitters.Loops;

using static Compiler.IlEmitterPrimitives;

internal readonly record struct RawI32ConditionPlan(
    RawI32ExpressionPlan Left,
    RawI32ExpressionPlan Right,
    string RuntimeMethod,
    int Fuel)
{
    public static bool TryCreate(
        Expression expression,
        LocalStackKindPlanner stackPlan,
        IReadOnlyDictionary<string, SandboxFunction> functions,
        out RawI32ConditionPlan condition)
    {
        condition = default;
        if (expression is not BinaryExpression { Operator: "==" or "!=" or "<" or "<=" or ">" or ">=" } binary ||
            !RawI32ExpressionPlan.TryCreate(binary.Left, stackPlan, functions, out var left) ||
            !RawI32ExpressionPlan.TryCreate(binary.Right, stackPlan, functions, out var right))
        {
            return false;
        }

        condition = new RawI32ConditionPlan(left, right, Method(binary.Operator), 1 + left.FuelCost + right.FuelCost);
        return true;
    }

    public void Emit(ILGenerator il, Func<string, (LocalBuilder Local, StackKind Kind)> declare)
    {
        Left.Emit(il, declare);
        Right.Emit(il, declare);
        il.Emit(OpCodes.Call, Runtime(RuntimeMethod));
    }

    private static string Method(string op)
        => op switch
        {
            "<" => nameof(CompiledRuntime.LtI32Raw),
            "<=" => nameof(CompiledRuntime.LteI32Raw),
            ">" => nameof(CompiledRuntime.GtI32Raw),
            ">=" => nameof(CompiledRuntime.GteI32Raw),
            "==" => nameof(CompiledRuntime.EqI32Raw),
            _ => nameof(CompiledRuntime.NeI32Raw)
        };
}
