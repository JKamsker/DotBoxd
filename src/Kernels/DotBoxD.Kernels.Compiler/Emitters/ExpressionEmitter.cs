using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Compiler.Emitters;

using System.Reflection;
using System.Reflection.Emit;
using DotBoxD.Kernels;
using DotBoxD.Kernels.Runtime;
using static DotBoxD.Kernels.Compiler.IlEmitterPrimitives;

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
        if (NumericConversionCallEmitter.TryEmitRaw(expression, want, _stackPlan, _il, EmitAs))
        {
            return;
        }

        if (want == StackKind.I32 && I32MathIntrinsicEmitter.TryEmit(expression, _bindings, _il, EmitAs))
        {
            return;
        }

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
                _il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.I32)));
                break;
            case (StackKind.I64, StackKind.Boxed):
                _il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.I64)));
                break;
            case (StackKind.F64, StackKind.Boxed):
                _il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.F64)));
                break;
            case (StackKind.Bool, StackKind.Boxed):
                _il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.Bool)));
                break;
            case (StackKind.Boxed, StackKind.I32):
                _il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.AsI32)));
                break;
            case (StackKind.Boxed, StackKind.I64):
                _il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.AsI64)));
                break;
            case (StackKind.Boxed, StackKind.F64):
                _il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.AsF64)));
                break;
            case (StackKind.Boxed, StackKind.Bool):
                _il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.AsBool)));
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

        if (value is I64Value i64)
        {
            _il.Emit(OpCodes.Ldc_I8, i64.Value);
            return StackKind.I64;
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
        if (RawUnaryEmitter.TryEmit(unary, _stackPlan, _il, EmitAs, out var kind))
        {
            return kind;
        }

        EmitAs(unary.Operand, StackKind.Boxed);
        var method = unary.Operator switch
        {
            "!" => nameof(Kernels.Runtime.CompiledRuntime.NotBool),
            "-" => nameof(Kernels.Runtime.CompiledRuntime.Neg),
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
                "+" => nameof(Kernels.Runtime.CompiledRuntime.AddI32Raw),
                "-" => nameof(Kernels.Runtime.CompiledRuntime.SubI32Raw),
                "*" => nameof(Kernels.Runtime.CompiledRuntime.MulI32Raw),
                "/" => nameof(Kernels.Runtime.CompiledRuntime.DivI32Raw),
                "%" => nameof(Kernels.Runtime.CompiledRuntime.RemI32Raw),
                _ => throw Unsupported("operator not supported by compiler")
            };
            _il.Emit(OpCodes.Call, Runtime(raw));
            return StackKind.I32;
        }

        if (binary.Operator is "+" or "-" or "*" or "/" or "%" &&
            _stackPlan.Infer(binary.Left) is { Name: "I64" } &&
            _stackPlan.Infer(binary.Right) is { Name: "I64" })
        {
            EmitAs(binary.Left, StackKind.I64);
            EmitAs(binary.Right, StackKind.I64);
            var raw = binary.Operator switch
            {
                "+" => nameof(Kernels.Runtime.CompiledRuntime.AddI64Raw),
                "-" => nameof(Kernels.Runtime.CompiledRuntime.SubI64Raw),
                "*" => nameof(Kernels.Runtime.CompiledRuntime.MulI64Raw),
                "/" => nameof(Kernels.Runtime.CompiledRuntime.DivI64Raw),
                "%" => nameof(Kernels.Runtime.CompiledRuntime.RemI64Raw),
                _ => throw Unsupported("operator not supported by compiler")
            };
            _il.Emit(OpCodes.Call, Runtime(raw));
            return StackKind.I64;
        }

        if (binary.Operator is "+" or "-" or "*" or "/" &&
            _stackPlan.Infer(binary.Left) is { Name: "F64" } &&
            _stackPlan.Infer(binary.Right) is { Name: "F64" })
        {
            EmitAs(binary.Left, StackKind.F64);
            EmitAs(binary.Right, StackKind.F64);
            var raw = binary.Operator switch
            {
                "+" => nameof(Kernels.Runtime.CompiledRuntime.AddF64Raw),
                "-" => nameof(Kernels.Runtime.CompiledRuntime.SubF64Raw),
                "*" => nameof(Kernels.Runtime.CompiledRuntime.MulF64Raw),
                "/" => nameof(Kernels.Runtime.CompiledRuntime.DivF64Raw),
                _ => throw Unsupported("operator not supported by compiler")
            };
            _il.Emit(OpCodes.Call, Runtime(raw));
            return StackKind.F64;
        }

        if (binary.Operator is "==" or "!=" or "<" or "<=" or ">" or ">=" &&
            _stackPlan.Infer(binary.Left) is { Name: "I32" } &&
            _stackPlan.Infer(binary.Right) is { Name: "I32" })
        {
            EmitAs(binary.Left, StackKind.I32);
            EmitAs(binary.Right, StackKind.I32);
            var raw = binary.Operator switch
            {
                "<" => nameof(Kernels.Runtime.CompiledRuntime.LtI32Raw),
                "<=" => nameof(Kernels.Runtime.CompiledRuntime.LteI32Raw),
                ">" => nameof(Kernels.Runtime.CompiledRuntime.GtI32Raw),
                ">=" => nameof(Kernels.Runtime.CompiledRuntime.GteI32Raw),
                "==" => nameof(Kernels.Runtime.CompiledRuntime.EqI32Raw),
                "!=" => nameof(Kernels.Runtime.CompiledRuntime.NeI32Raw),
                _ => throw Unsupported("operator not supported by compiler")
            };
            _il.Emit(OpCodes.Call, Runtime(raw));
            return StackKind.Bool;
        }

        if (binary.Operator is "==" or "!=" or "<" or "<=" or ">" or ">=" &&
            _stackPlan.Infer(binary.Left) is { Name: "I64" } &&
            _stackPlan.Infer(binary.Right) is { Name: "I64" })
        {
            EmitAs(binary.Left, StackKind.I64);
            EmitAs(binary.Right, StackKind.I64);
            var raw = binary.Operator switch
            {
                "<" => nameof(Kernels.Runtime.CompiledRuntime.LtI64Raw),
                "<=" => nameof(Kernels.Runtime.CompiledRuntime.LteI64Raw),
                ">" => nameof(Kernels.Runtime.CompiledRuntime.GtI64Raw),
                ">=" => nameof(Kernels.Runtime.CompiledRuntime.GteI64Raw),
                "==" => nameof(Kernels.Runtime.CompiledRuntime.EqI64Raw),
                "!=" => nameof(Kernels.Runtime.CompiledRuntime.NeI64Raw),
                _ => throw Unsupported("operator not supported by compiler")
            };
            _il.Emit(OpCodes.Call, Runtime(raw));
            return StackKind.Bool;
        }

        if (binary.Operator is "==" or "!=" or "<" or "<=" or ">" or ">=" &&
            _stackPlan.Infer(binary.Left) is { Name: "F64" } &&
            _stackPlan.Infer(binary.Right) is { Name: "F64" })
        {
            EmitAs(binary.Left, StackKind.F64);
            EmitAs(binary.Right, StackKind.F64);
            var raw = binary.Operator switch
            {
                "<" => nameof(Kernels.Runtime.CompiledRuntime.LtF64Raw),
                "<=" => nameof(Kernels.Runtime.CompiledRuntime.LteF64Raw),
                ">" => nameof(Kernels.Runtime.CompiledRuntime.GtF64Raw),
                ">=" => nameof(Kernels.Runtime.CompiledRuntime.GteF64Raw),
                "==" => nameof(Kernels.Runtime.CompiledRuntime.EqF64Raw),
                "!=" => nameof(Kernels.Runtime.CompiledRuntime.NeF64Raw),
                _ => throw Unsupported("operator not supported by compiler")
            };
            _il.Emit(OpCodes.Call, Runtime(raw));
            return StackKind.Bool;
        }

        EmitAs(binary.Left, StackKind.Boxed);
        EmitAs(binary.Right, StackKind.Boxed);
        var method = binary.Operator switch
        {
            "+" => nameof(Kernels.Runtime.CompiledRuntime.Add),
            "-" => nameof(Kernels.Runtime.CompiledRuntime.Sub),
            "*" => nameof(Kernels.Runtime.CompiledRuntime.Mul),
            "/" => nameof(Kernels.Runtime.CompiledRuntime.Div),
            "%" => nameof(Kernels.Runtime.CompiledRuntime.Rem),
            "==" => nameof(Kernels.Runtime.CompiledRuntime.Eq),
            "!=" => nameof(Kernels.Runtime.CompiledRuntime.Ne),
            "<" => nameof(Kernels.Runtime.CompiledRuntime.Lt),
            "<=" => nameof(Kernels.Runtime.CompiledRuntime.Lte),
            ">" => nameof(Kernels.Runtime.CompiledRuntime.Gt),
            ">=" => nameof(Kernels.Runtime.CompiledRuntime.Gte),
            _ => throw Unsupported("operator not supported by compiler")
        };
        _il.Emit(OpCodes.Call, Runtime(method));
        return StackKind.Boxed;
    }

    private void EmitCall(CallExpression call)
    {
        if (NumericConversionCallEmitter.TryEmit(call, _stackPlan, _il, EmitAs))
        {
            return;
        }

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
