using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Compiler.Emitters.Returns;

using System.Reflection.Emit;

internal static class F64RawReturnEmitter
{
    public static bool TryEmit(
        Expression expression,
        SandboxType returnType,
        IBindingCatalog bindings,
        ILGenerator il,
        ExpressionEmitter expressions)
    {
        if (!returnType.Equals(SandboxType.F64) ||
            !F64MathIntrinsicEmitter.TryEmit(expression, bindings, il, expressions.EmitAs))
        {
            return false;
        }

        expressions.Coerce(StackKind.F64, StackKind.Boxed);
        return true;
    }
}
