namespace SafeIR.Compiler.Emitters;

using System.Reflection.Emit;
using SafeIR;
using SafeIR.Runtime;
using static SafeIR.Compiler.IlEmitterPrimitives;

internal static class ShortCircuitBooleanEmitter
{
    public static void Emit(
        BinaryExpression binary,
        ILGenerator il,
        IBindingCatalog bindings,
        IReadOnlyDictionary<string, FunctionAnalysis> functionAnalysis,
        Action<Expression> emitExpression)
    {
        var order = ShortCircuitExpressionOrder.Choose(binary, bindings, functionAnalysis);
        var shortcutLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        emitExpression(order.First);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.AsBool)));
        il.Emit(binary.Operator == "&&" ? OpCodes.Brfalse : OpCodes.Brtrue, shortcutLabel);

        emitExpression(order.Second);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(shortcutLabel);
        EmitInt32(il, binary.Operator == "&&" ? 0 : 1);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.Bool)));

        il.MarkLabel(endLabel);
    }
}
