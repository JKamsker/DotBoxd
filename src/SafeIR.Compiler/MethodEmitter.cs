namespace SafeIR.Compiler;

using System.Reflection;
using System.Reflection.Emit;
using SafeIR;
using SafeIR.Runtime;
using static IlEmitterPrimitives;

internal sealed class MethodEmitter
{
    private readonly ILGenerator _il;
    private readonly SandboxFunction _function;
    private readonly IReadOnlyDictionary<string, MethodInfo> _functions;
    private readonly IBindingCatalog _bindings;
    private readonly Dictionary<string, LocalBuilder> _locals = new(StringComparer.Ordinal);

    public MethodEmitter(
        ILGenerator il,
        SandboxFunction function,
        IReadOnlyDictionary<string, MethodInfo> functions,
        IBindingCatalog bindings)
    {
        _il = il;
        _function = function;
        _functions = functions;
        _bindings = bindings;
    }

    public void Emit()
    {
        EmitEnterCall();
        EmitFuel(1);
        EmitParameters();
        foreach (var statement in _function.Body) {
            EmitStatement(statement);
        }

        EmitExitCall();
        _il.Emit(OpCodes.Ldnull);
        _il.Emit(OpCodes.Ret);
    }

    private void EmitParameters()
    {
        for (var i = 0; i < _function.Parameters.Count; i++) {
            var local = Declare(_function.Parameters[i].Name);
            _il.Emit(OpCodes.Ldarg, i + 1);
            _il.Emit(OpCodes.Stloc, local);
        }
    }

    private void EmitStatement(Statement statement)
    {
        EmitFuel(1);
        switch (statement) {
            case AssignmentStatement assignment:
                EmitExpression(assignment.Value);
                _il.Emit(OpCodes.Stloc, Declare(assignment.Name));
                break;
            case ReturnStatement ret:
                EmitExpression(ret.Value);
                EmitReturnValue();
                break;
            case IfStatement branch:
                EmitIf(branch);
                break;
            case ForRangeStatement range:
                EmitForRange(range);
                break;
            case WhileStatement loop:
                EmitWhile(loop);
                break;
            default:
                EmitUnsupported("statement not supported");
                break;
        }
    }

    private void EmitIf(IfStatement branch)
    {
        var elseLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();
        EmitExpression(branch.Condition);
        _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.AsBool)));
        _il.Emit(OpCodes.Brfalse, elseLabel);
        branch.Then.ToList().ForEach(EmitStatement);
        _il.Emit(OpCodes.Br, endLabel);
        _il.MarkLabel(elseLabel);
        branch.Else.ToList().ForEach(EmitStatement);
        _il.MarkLabel(endLabel);
    }

    private void EmitForRange(ForRangeStatement range)
    {
        var index = _il.DeclareLocal(typeof(int));
        var end = _il.DeclareLocal(typeof(int));
        EmitExpression(range.Start);
        _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.AsI32)));
        _il.Emit(OpCodes.Stloc, index);
        EmitExpression(range.End);
        _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.AsI32)));
        _il.Emit(OpCodes.Stloc, end);

        var startLabel = _il.DefineLabel();
        var finishLabel = _il.DefineLabel();
        _il.MarkLabel(startLabel);
        _il.Emit(OpCodes.Ldloc, index);
        _il.Emit(OpCodes.Ldloc, end);
        _il.Emit(OpCodes.Bge, finishLabel);
        EmitFuel(5);
        _il.Emit(OpCodes.Ldloc, index);
        _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.I32)));
        _il.Emit(OpCodes.Stloc, Declare(range.LocalName));
        range.Body.ToList().ForEach(EmitStatement);
        _il.Emit(OpCodes.Ldloc, index);
        EmitInt32(_il, 1);
        _il.Emit(OpCodes.Add);
        _il.Emit(OpCodes.Stloc, index);
        _il.Emit(OpCodes.Br, startLabel);
        _il.MarkLabel(finishLabel);
    }

    private void EmitWhile(WhileStatement loop)
    {
        var startLabel = _il.DefineLabel();
        var finishLabel = _il.DefineLabel();
        _il.MarkLabel(startLabel);
        EmitExpression(loop.Condition);
        _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.AsBool)));
        _il.Emit(OpCodes.Brfalse, finishLabel);
        EmitFuel(5);
        loop.Body.ToList().ForEach(EmitStatement);
        _il.Emit(OpCodes.Br, startLabel);
        _il.MarkLabel(finishLabel);
    }

    private void EmitExpression(Expression expression)
    {
        switch (expression) {
            case LiteralExpression literal:
                EmitLiteral(literal.Value);
                break;
            case VariableExpression variable:
                _il.Emit(OpCodes.Ldloc, _locals[variable.Name]);
                break;
            case UnaryExpression unary:
                EmitUnary(unary);
                break;
            case BinaryExpression binary:
                EmitBinary(binary);
                break;
            case CallExpression call:
                EmitCall(call);
                break;
            default:
                EmitUnsupported("expression not supported");
                break;
        }
    }

    private void EmitLiteral(SandboxValue value)
    {
        switch (value) {
            case I32Value i32:
                EmitInt32(_il, i32.Value);
                _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.I32)));
                break;
            case BoolValue boolean:
                EmitInt32(_il, boolean.Value ? 1 : 0);
                _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.Bool)));
                break;
            case F64Value f64:
                _il.Emit(OpCodes.Ldc_R8, f64.Value);
                _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.F64)));
                break;
            case StringValue text:
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldstr, text.Value);
                _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.StringConst)));
                break;
            default:
                EmitUnsupported("literal not supported by compiler");
                break;
        }
    }

    private void EmitUnary(UnaryExpression unary)
    {
        EmitExpression(unary.Operand);
        var method = unary.Operator switch {
            "!" => nameof(CompiledRuntime.NotBool),
            "-" => nameof(CompiledRuntime.NegI32),
            _ => throw Unsupported("unary operator not supported by compiler")
        };
        _il.Emit(OpCodes.Call, Runtime(method));
    }

    private void EmitBinary(BinaryExpression binary)
    {
        EmitExpression(binary.Left);
        EmitExpression(binary.Right);
        var method = binary.Operator switch {
            "+" => nameof(CompiledRuntime.AddI32),
            "-" => nameof(CompiledRuntime.SubI32),
            "*" => nameof(CompiledRuntime.MulI32),
            "/" => nameof(CompiledRuntime.DivI32),
            "%" => nameof(CompiledRuntime.RemI32),
            "==" => nameof(CompiledRuntime.Eq),
            "!=" => nameof(CompiledRuntime.Ne),
            "<" => nameof(CompiledRuntime.LtI32),
            "<=" => nameof(CompiledRuntime.LteI32),
            ">" => nameof(CompiledRuntime.GtI32),
            ">=" => nameof(CompiledRuntime.GteI32),
            "&&" => nameof(CompiledRuntime.And),
            "||" => nameof(CompiledRuntime.Or),
            _ => throw Unsupported("operator not supported by compiler")
        };
        _il.Emit(OpCodes.Call, Runtime(method));
    }

    private void EmitCall(CallExpression call)
    {
        if (PureBindingCallEmitter.TryEmit(call, _il, EmitExpression)) {
            return;
        }

        if (_functions.TryGetValue(call.Name, out var method)) {
            EmitFunctionCall(call, method);
            return;
        }

        if (!BindingCallEmitter.TryEmit(call, _bindings, _il, EmitExpression)) {
            EmitUnsupported($"call '{call.Name}' is not supported by compiler");
        }
    }

    private void EmitFunctionCall(CallExpression call, MethodInfo method)
    {
        _il.Emit(OpCodes.Ldarg_0);
        foreach (var argument in call.Arguments) {
            EmitExpression(argument);
        }

        _il.Emit(OpCodes.Call, method);
    }

    private LocalBuilder Declare(string name)
    {
        if (_locals.TryGetValue(name, out var existing)) {
            return existing;
        }

        var local = _il.DeclareLocal(typeof(SandboxValue));
        _locals[name] = local;
        return local;
    }

    private void EmitFuel(int amount)
    {
        _il.Emit(OpCodes.Ldarg_0);
        EmitInt32(_il, amount);
        _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.ChargeFuel)));
    }

    private void EmitEnterCall()
    {
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.EnterCall)));
    }

    private void EmitExitCall()
    {
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.ExitCall)));
    }

    private void EmitReturnValue()
    {
        var value = _il.DeclareLocal(typeof(SandboxValue));
        _il.Emit(OpCodes.Stloc, value);
        EmitExitCall();
        _il.Emit(OpCodes.Ldloc, value);
        EmitSandboxType(_il, _function.ReturnType);
        _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.RequireValueType)));
        _il.Emit(OpCodes.Ret);
    }

    private void EmitUnsupported(string message) => throw Unsupported(message);

    private static Exception Unsupported(string message)
        => new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, message));
}
