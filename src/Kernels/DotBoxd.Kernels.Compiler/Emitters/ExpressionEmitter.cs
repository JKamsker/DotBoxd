namespace DotBoxd.Kernels.Compiler.Emitters;

using System.Reflection;
using System.Reflection.Emit;
using DotBoxd.Kernels;
using DotBoxd.Kernels.Runtime;
using static DotBoxd.Kernels.Compiler.IlEmitterPrimitives;

internal sealed class ExpressionEmitter
{
    private readonly ILGenerator _il;
    private readonly IReadOnlyDictionary<string, MethodInfo> _functions;
    private readonly IBindingCatalog _bindings;
    private readonly IReadOnlyDictionary<string, FunctionAnalysis> _functionAnalysis;
    private readonly IReadOnlyDictionary<string, (LocalBuilder Local, StackKind Kind)> _locals;
    private readonly LocalStackKindPlanner _stackPlan;

    public ExpressionEmitter(
        ILGenerator il,
        IReadOnlyDictionary<string, MethodInfo> functions,
        IBindingCatalog bindings,
        IReadOnlyDictionary<string, FunctionAnalysis> functionAnalysis,
        IReadOnlyDictionary<string, (LocalBuilder Local, StackKind Kind)> locals,
        LocalStackKindPlanner stackPlan)
    {
        _il = il;
        _functions = functions;
        _bindings = bindings;
        _functionAnalysis = functionAnalysis;
        _locals = locals;
        _stackPlan = stackPlan;
    }

    public void EmitAs(Expression expression, StackKind want)
    {
        if (want == StackKind.F64 && F64MathIntrinsicEmitter.TryEmit(expression, _bindings, _il, EmitAs))
        {
            return;
        }

        Coerce(EmitValue(expression), want);
    }

    public void Coerce(StackKind have, StackKind want)
    {
        if (have == want)
        {
            return;
        }

        switch (have, want)
        {
            case (StackKind.I32, StackKind.Boxed):
                _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.I32)));
                break;
            case (StackKind.F64, StackKind.Boxed):
                _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.F64)));
                break;
            case (StackKind.Boxed, StackKind.I32):
                _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.AsI32)));
                break;
            case (StackKind.Boxed, StackKind.F64):
                _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.AsF64)));
                break;
            case (StackKind.Boxed, StackKind.Bool):
                _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.AsBool)));
                break;
            default:
                throw Unsupported($"cannot coerce {have} to {want}");
        }
    }

    public StackKind EmitValue(Expression expression)
    {
        CompiledMeterEmitter.Fuel(_il, 1);
        switch (expression)
        {
            case LiteralExpression literal:
                return EmitLiteral(literal.Value);
            case VariableExpression variable:
                var (local, kind) = _locals[variable.Name];
                _il.Emit(OpCodes.Ldloc, local);
                return kind;
            case UnaryExpression unary:
                return EmitUnary(unary);
            case BinaryExpression binary:
                return EmitBinary(binary);
            case CallExpression call:
                EmitCall(call);
                return StackKind.Boxed;
            default:
                throw Unsupported("expression not supported");
        }
    }

    private StackKind EmitLiteral(SandboxValue value)
    {
        if (value is I32Value i32)
        {
            EmitInt32(_il, i32.Value);
            return StackKind.I32;
        }

        if (value is F64Value f64)
        {
            _il.Emit(OpCodes.Ldc_R8, f64.Value);
            return StackKind.F64;
        }

        CompiledLiteralEmitter.Emit(_il, value);
        return StackKind.Boxed;
    }

    private StackKind EmitUnary(UnaryExpression unary)
    {
        if (unary.Operator == "-" && _stackPlan.Infer(unary.Operand) is { Name: "I32" })
        {
            EmitAs(unary.Operand, StackKind.I32);
            _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.NegI32Raw)));
            return StackKind.I32;
        }

        EmitAs(unary.Operand, StackKind.Boxed);
        var method = unary.Operator switch
        {
            "!" => nameof(CompiledRuntime.NotBool),
            "-" => nameof(CompiledRuntime.Neg),
            _ => throw Unsupported("unary operator not supported by compiler")
        };
        _il.Emit(OpCodes.Call, Runtime(method));
        return StackKind.Boxed;
    }

    private StackKind EmitBinary(BinaryExpression binary)
    {
        if (binary.Operator is "&&" or "||")
        {
            ShortCircuitBooleanEmitter.Emit(binary, _il, _bindings, _functionAnalysis, e => EmitAs(e, StackKind.Boxed));
            return StackKind.Boxed;
        }

        if (binary.Operator is "+" or "-" or "*" or "/" or "%" && _stackPlan.Infer(binary.Left) is { Name: "I32" })
        {
            EmitAs(binary.Left, StackKind.I32);
            EmitAs(binary.Right, StackKind.I32);
            var raw = binary.Operator switch
            {
                "+" => nameof(CompiledRuntime.AddI32Raw),
                "-" => nameof(CompiledRuntime.SubI32Raw),
                "*" => nameof(CompiledRuntime.MulI32Raw),
                "/" => nameof(CompiledRuntime.DivI32Raw),
                "%" => nameof(CompiledRuntime.RemI32Raw),
                _ => throw Unsupported("operator not supported by compiler")
            };
            _il.Emit(OpCodes.Call, Runtime(raw));
            return StackKind.I32;
        }

        EmitAs(binary.Left, StackKind.Boxed);
        EmitAs(binary.Right, StackKind.Boxed);
        var method = binary.Operator switch
        {
            "+" => nameof(CompiledRuntime.Add),
            "-" => nameof(CompiledRuntime.Sub),
            "*" => nameof(CompiledRuntime.Mul),
            "/" => nameof(CompiledRuntime.Div),
            "%" => nameof(CompiledRuntime.Rem),
            "==" => nameof(CompiledRuntime.Eq),
            "!=" => nameof(CompiledRuntime.Ne),
            "<" => nameof(CompiledRuntime.Lt),
            "<=" => nameof(CompiledRuntime.Lte),
            ">" => nameof(CompiledRuntime.Gt),
            ">=" => nameof(CompiledRuntime.Gte),
            _ => throw Unsupported("operator not supported by compiler")
        };
        _il.Emit(OpCodes.Call, Runtime(method));
        return StackKind.Boxed;
    }

    private void EmitCall(CallExpression call)
    {
        if (PureBindingCallEmitter.TryEmit(call, _il, e => EmitAs(e, StackKind.Boxed)))
        {
            return;
        }

        if (_functions.TryGetValue(call.Name, out var method))
        {
            EmitFunctionCall(call, method);
            return;
        }

        if (!BindingCallEmitter.TryEmit(call, _bindings, _il, e => EmitAs(e, StackKind.Boxed)))
        {
            throw Unsupported($"call '{call.Name}' is not supported by compiler");
        }
    }

    private void EmitFunctionCall(CallExpression call, MethodInfo method)
    {
        _il.Emit(OpCodes.Ldarg_0);
        foreach (var argument in call.Arguments)
        {
            EmitAs(argument, StackKind.Boxed);
        }

        _il.Emit(OpCodes.Call, method);
    }

    private static Exception Unsupported(string message)
        => new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, message));
}
