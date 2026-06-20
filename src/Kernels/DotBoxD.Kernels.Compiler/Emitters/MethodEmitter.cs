using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Compiler.Emitters.Loops;
using DotBoxD.Kernels.Compiler.Emitters.Returns;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Compiler.Emitters;

using System.Reflection;
using System.Reflection.Emit;
using DotBoxD.Kernels;
using static DotBoxD.Kernels.Compiler.IlEmitterPrimitives;

internal sealed class MethodEmitter
{
    private readonly ILGenerator _il;
    private readonly SandboxFunction _function;
    private readonly IReadOnlyDictionary<string, SandboxFunction> _functionModels;
    private readonly IBindingCatalog _bindings;
    private readonly Dictionary<string, (LocalBuilder Local, StackKind Kind)> _locals = new(StringComparer.Ordinal);
    private readonly HashSet<string> _nonNegativeF64Locals = new(StringComparer.Ordinal);
    private readonly LocalStackKindPlanner _stackPlan;
    private readonly ExpressionEmitter _expressions;

    public MethodEmitter(
        ILGenerator il,
        SandboxFunction function,
        IReadOnlyDictionary<string, MethodInfo> functions,
        IReadOnlyDictionary<string, SandboxFunction> functionModels,
        IBindingCatalog bindings,
        IReadOnlyDictionary<string, FunctionAnalysis> functionAnalysis)
    {
        _il = il;
        _function = function;
        _functionModels = functionModels;
        _bindings = bindings;
        _stackPlan = new LocalStackKindPlanner(function, bindings, functionAnalysis);
        _expressions = new ExpressionEmitter(il, functions, bindings, functionAnalysis, _locals, _stackPlan);
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
            var (local, kind) = Declare(_function.Parameters[i].Name);
            _il.Emit(OpCodes.Ldarg, i + 1);
            _expressions.Coerce(StackKind.Boxed, kind);
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
                var (local, kind) = Declare(assignment.Name);
                _expressions.EmitAs(assignment.Value, kind);
                _il.Emit(OpCodes.Stloc, local);
                UpdateF64Facts(assignment.Name, assignment.Value);
                return false;
            case ReturnStatement ret:
                if (BoolReturnFastPathEmitter.TryEmit(ret.Value, _il, _stackPlan, Declare) ||
                    F64RawReturnEmitter.TryEmit(ret.Value, _function.ReturnType, _bindings, _il, _expressions))
                {
                    EmitReturnValue();
                    return true;
                }

                _expressions.EmitAs(ret.Value, StackKind.Boxed);
                EmitReturnValue();
                return true;
            case ExpressionStatement expression:
                _expressions.EmitValue(expression.Value);
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
        _nonNegativeF64Locals.Clear();
        var elseLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();
        _expressions.EmitAs(branch.Condition, StackKind.Bool);
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

        _nonNegativeF64Locals.Clear();
        return thenReturns && elseReturns;
    }

    private void EmitForRange(ForRangeStatement range)
    {
        if (F64LoopFastPathEmitter.TryEmit(
            range,
            _il,
            _stackPlan,
            _bindings,
            _nonNegativeF64Locals,
            Declare,
            out var nonNegativeTarget))
        {
            _nonNegativeF64Locals.Clear();
            if (nonNegativeTarget is not null)
            {
                _nonNegativeF64Locals.Add(nonNegativeTarget);
            }

            return;
        }

        if (BranchedF64LoopFastPathEmitter.TryEmit(
            range,
            _il,
            _stackPlan,
            _expressions,
            _functionModels,
            _bindings,
            _nonNegativeF64Locals,
            Declare))
        {
            _nonNegativeF64Locals.Clear();
            return;
        }

        if (MapGetI32LoopFastPathEmitter.TryEmit(range, _il, _stackPlan, Declare))
        {
            _nonNegativeF64Locals.Clear();
            return;
        }

        if (ListGetI32LoopFastPathEmitter.TryEmit(range, _il, _stackPlan, Declare))
        {
            _nonNegativeF64Locals.Clear();
            return;
        }

        if (ListCountLoopFastPathEmitter.TryEmit(range, _il, _stackPlan, Declare))
        {
            _nonNegativeF64Locals.Clear();
            return;
        }

        if (StringLengthLoopFastPathEmitter.TryEmit(range, _il, _stackPlan, _bindings, Declare))
        {
            _nonNegativeF64Locals.Clear();
            return;
        }

        if (I32LoopFastPathEmitter.TryEmit(range, _il, _stackPlan, _expressions, _functionModels, _bindings, Declare))
        {
            _nonNegativeF64Locals.Clear();
            return;
        }

        if (BranchedI32LoopFastPathEmitter.TryEmit(range, _il, _stackPlan, _expressions, _functionModels, Declare))
        {
            _nonNegativeF64Locals.Clear();
            return;
        }

        if (I64LoopFastPathEmitter.TryEmit(range, _il, _stackPlan, _expressions, Declare))
        {
            _nonNegativeF64Locals.Clear();
            return;
        }

        _nonNegativeF64Locals.Clear();
        var index = _il.DeclareLocal(typeof(int));
        var end = _il.DeclareLocal(typeof(int));
        _expressions.EmitAs(range.Start, StackKind.I32);
        _il.Emit(OpCodes.Stloc, index);
        _expressions.EmitAs(range.End, StackKind.I32);
        _il.Emit(OpCodes.Stloc, end);

        var startLabel = _il.DefineLabel();
        var finishLabel = _il.DefineLabel();
        _il.MarkLabel(startLabel);
        _il.Emit(OpCodes.Ldloc, index);
        _il.Emit(OpCodes.Ldloc, end);
        _il.Emit(OpCodes.Bge, finishLabel);
        CompiledMeterEmitter.LoopIteration(_il, 5);
        var (loopVar, loopKind) = Declare(range.LocalName);
        _il.Emit(OpCodes.Ldloc, index);
        _expressions.Coerce(StackKind.I32, loopKind);
        _il.Emit(OpCodes.Stloc, loopVar);
        EmitBlock(range.Body);
        _il.Emit(OpCodes.Ldloc, index);
        EmitInt32(_il, 1);
        _il.Emit(OpCodes.Add);
        _il.Emit(OpCodes.Stloc, index);
        _il.Emit(OpCodes.Br, startLabel);
        _il.MarkLabel(finishLabel);
        _nonNegativeF64Locals.Clear();
    }

    private void EmitWhile(WhileStatement loop)
    {
        _nonNegativeF64Locals.Clear();
        if (WhileI32LoopFastPathEmitter.TryEmit(loop, _il, _stackPlan, _expressions, _functionModels, Declare))
        {
            _nonNegativeF64Locals.Clear();
            return;
        }

        var startLabel = _il.DefineLabel();
        var finishLabel = _il.DefineLabel();
        _il.MarkLabel(startLabel);
        _expressions.EmitAs(loop.Condition, StackKind.Bool);
        _il.Emit(OpCodes.Brfalse, finishLabel);
        CompiledMeterEmitter.LoopIteration(_il, 5);
        EmitBlock(loop.Body);
        _nonNegativeF64Locals.Clear();
        _il.Emit(OpCodes.Br, startLabel);
        _il.MarkLabel(finishLabel);
    }

    private (LocalBuilder Local, StackKind Kind) Declare(string name)
    {
        if (_locals.TryGetValue(name, out var existing))
        {
            return existing;
        }

        var kind = _stackPlan.LocalKind(name);
        var local = _il.DeclareLocal(LocalType(kind));
        var entry = (local, kind);
        _locals[name] = entry;
        return entry;
    }

    private static Type LocalType(StackKind kind)
        => kind switch
        {
            StackKind.I32 => typeof(int),
            StackKind.I64 => typeof(long),
            StackKind.F64 => typeof(double),
            _ => typeof(SandboxValue)
        };

    private void EmitReturnValue()
    {
        var value = _il.DeclareLocal(typeof(SandboxValue));
        _il.Emit(OpCodes.Stloc, value);
        _il.Emit(OpCodes.Ldloc, value);
        CompiledTypeEmitter.EmitMetered(_il, _function.ReturnType);
        _il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.RequireValueType)));
        _il.Emit(OpCodes.Stloc, value);
        CompiledMeterEmitter.ExitCall(_il);
        _il.Emit(OpCodes.Ldloc, value);
        _il.Emit(OpCodes.Ret);
    }

    private static Exception Unsupported(string message)
        => new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, message));

    private void UpdateF64Facts(string name, Expression expression)
    {
        if (_stackPlan.LocalKind(name) == StackKind.F64 && IsNonNegativeF64(expression))
        {
            _nonNegativeF64Locals.Add(name);
        }
        else
        {
            _nonNegativeF64Locals.Remove(name);
        }
    }

    private bool IsNonNegativeF64(Expression expression)
        => expression switch
        {
            LiteralExpression { Value: F64Value value } => value.Value >= 0,
            VariableExpression variable => _nonNegativeF64Locals.Contains(variable.Name),
            CallExpression { Name: "math.sqrt", Arguments.Count: 1 } call => IsNonNegativeF64(call.Arguments[0]),
            CallExpression { Name: "math.floor" or "math.ceil" or "math.round", Arguments.Count: 1 } call
                => IsNonNegativeF64(call.Arguments[0]),
            _ => false
        };
}
