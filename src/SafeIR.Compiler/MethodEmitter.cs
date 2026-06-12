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
    private readonly IReadOnlyDictionary<string, FunctionAnalysis> _functionAnalysis;
    private readonly Dictionary<string, LocalBuilder> _locals = new(StringComparer.Ordinal);

    public MethodEmitter(
        ILGenerator il,
        SandboxFunction function,
        IReadOnlyDictionary<string, MethodInfo> functions,
        IBindingCatalog bindings,
        IReadOnlyDictionary<string, FunctionAnalysis> functionAnalysis)
    {
        _il = il;
        _function = function;
        _functions = functions;
        _bindings = bindings;
        _functionAnalysis = functionAnalysis;
    }

    public void Emit()
    {
        CompiledMeterEmitter.EnterCall(_il);
        CompiledMeterEmitter.Fuel(_il, 1);
        EmitParameters();
        var returned = EmitBlock(_function.Body);

        if (!returned)
        {
            CompiledMeterEmitter.ExitCall(_il);
            _il.Emit(OpCodes.Ldnull);
            _il.Emit(OpCodes.Ret);
        }
    }

    private void EmitParameters()
    {
        for (var i = 0; i < _function.Parameters.Count; i++)
        {
            var local = Declare(_function.Parameters[i].Name);
            _il.Emit(OpCodes.Ldarg, i + 1);
            _il.Emit(OpCodes.Stloc, local);
        }
    }

    private bool EmitBlock(IReadOnlyList<Statement> statements)
    {
        foreach (var statement in statements)
        {
            if (EmitStatement(statement))
            {
                return true;
            }
        }

        return false;
    }

    private bool EmitStatement(Statement statement)
    {
        CompiledMeterEmitter.Fuel(_il, 1);
        switch (statement)
        {
            case AssignmentStatement assignment:
                EmitExpression(assignment.Value);
                _il.Emit(OpCodes.Stloc, Declare(assignment.Name));
                return false;
            case ReturnStatement ret:
                EmitExpression(ret.Value);
                EmitReturnValue();
                return true;
            case ExpressionStatement expression:
                EmitExpression(expression.Value);
                _il.Emit(OpCodes.Pop);
                return false;
            case IfStatement branch:
                return EmitIf(branch);
            case ForRangeStatement range:
                EmitForRange(range);
                return false;
            case WhileStatement loop:
                EmitWhile(loop);
                return false;
            default:
                throw Unsupported("statement not supported");
        }
    }

    private bool EmitIf(IfStatement branch)
    {
        var elseLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();
        EmitExpression(branch.Condition);
        _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.AsBool)));
        _il.Emit(OpCodes.Brfalse, elseLabel);
        var thenReturns = EmitBlock(branch.Then);
        if (!thenReturns)
        {
            _il.Emit(OpCodes.Br, endLabel);
        }

        _il.MarkLabel(elseLabel);
        var elseReturns = EmitBlock(branch.Else);
        if (!thenReturns || !elseReturns)
        {
            _il.MarkLabel(endLabel);
        }

        return thenReturns && elseReturns;
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
        CompiledMeterEmitter.LoopIteration(_il, 5);
        _il.Emit(OpCodes.Ldloc, index);
        _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.I32)));
        _il.Emit(OpCodes.Stloc, Declare(range.LocalName));
        EmitBlock(range.Body);
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
        CompiledMeterEmitter.LoopIteration(_il, 5);
        EmitBlock(loop.Body);
        _il.Emit(OpCodes.Br, startLabel);
        _il.MarkLabel(finishLabel);
    }

    private void EmitExpression(Expression expression)
    {
        CompiledMeterEmitter.Fuel(_il, 1);
        switch (expression)
        {
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
                throw Unsupported("expression not supported");
        }
    }

    private void EmitLiteral(SandboxValue value)
    {
        switch (value)
        {
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
                throw Unsupported("literal not supported by compiler");
        }
    }

    private void EmitUnary(UnaryExpression unary)
    {
        EmitExpression(unary.Operand);
        var method = unary.Operator switch
        {
            "!" => nameof(CompiledRuntime.NotBool),
            "-" => nameof(CompiledRuntime.NegI32),
            _ => throw Unsupported("unary operator not supported by compiler")
        };
        _il.Emit(OpCodes.Call, Runtime(method));
    }

    private void EmitBinary(BinaryExpression binary)
    {
        if (binary.Operator is "&&" or "||")
        {
            ShortCircuitBooleanEmitter.Emit(binary, _il, _bindings, _functionAnalysis, EmitExpression);
            return;
        }

        EmitExpression(binary.Left);
        EmitExpression(binary.Right);
        var method = binary.Operator switch
        {
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
            _ => throw Unsupported("operator not supported by compiler")
        };
        _il.Emit(OpCodes.Call, Runtime(method));
    }

    private void EmitCall(CallExpression call)
    {
        if (PureBindingCallEmitter.TryEmit(call, _il, EmitExpression))
        {
            return;
        }

        if (_functions.TryGetValue(call.Name, out var method))
        {
            EmitFunctionCall(call, method);
            return;
        }

        if (!BindingCallEmitter.TryEmit(call, _bindings, _il, EmitExpression))
        {
            throw Unsupported($"call '{call.Name}' is not supported by compiler");
        }
    }

    private void EmitFunctionCall(CallExpression call, MethodInfo method)
    {
        _il.Emit(OpCodes.Ldarg_0);
        foreach (var argument in call.Arguments)
        {
            EmitExpression(argument);
        }

        _il.Emit(OpCodes.Call, method);
    }

    private LocalBuilder Declare(string name)
    {
        if (_locals.TryGetValue(name, out var existing))
        {
            return existing;
        }

        var local = _il.DeclareLocal(typeof(SandboxValue));
        _locals[name] = local;
        return local;
    }

    private void EmitReturnValue()
    {
        var value = _il.DeclareLocal(typeof(SandboxValue));
        _il.Emit(OpCodes.Stloc, value);
        _il.Emit(OpCodes.Ldloc, value);
        EmitMeteredSandboxType(_function.ReturnType);
        _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.RequireValueType)));
        _il.Emit(OpCodes.Stloc, value);
        CompiledMeterEmitter.ExitCall(_il);
        _il.Emit(OpCodes.Ldloc, value);
        _il.Emit(OpCodes.Ret);
    }

    private void EmitMeteredSandboxType(SandboxType type)
    {
        if (type is { Name: "List", Arguments.Count: 1 })
        {
            EmitMeteredSandboxType(type.Arguments[0]);
            CompiledMeterEmitter.Fuel(_il, 1);
            _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.TypeList)));
            return;
        }

        if (type is { Name: "Map", Arguments.Count: 2 })
        {
            EmitMeteredSandboxType(type.Arguments[0]);
            EmitMeteredSandboxType(type.Arguments[1]);
            CompiledMeterEmitter.Fuel(_il, 1);
            _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.TypeMap)));
            return;
        }

        CompiledMeterEmitter.Fuel(_il, 1);
        _il.Emit(OpCodes.Ldstr, type.Name);
        _il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.TypeScalar)));
    }

    private static Exception Unsupported(string message)
        => new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, message));
}
