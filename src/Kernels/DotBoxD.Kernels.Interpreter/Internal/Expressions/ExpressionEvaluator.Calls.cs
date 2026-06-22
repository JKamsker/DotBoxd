using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter;

internal sealed partial class ExpressionEvaluator
{
    private ValueTask<SandboxValue> EvaluateCall(CallExpression call, InterpreterFrame frame)
    {
        if (UnaryPureIntrinsicDispatcher.IsCandidate(call.Name) &&
            UnaryPureIntrinsicDispatcher.TryEvaluate(
                call, this, frame, _context, _options, _moduleHash, frame.FunctionId, out var mathValue))
        {
            return mathValue;
        }

        var fixedArity = CollectionIntrinsicDispatcher.FixedArity(call.Name);
        if (fixedArity >= 0 && fixedArity == call.Arguments.Count)
        {
            return EvaluateFixedArityCollectionCall(call, fixedArity, frame);
        }

        return EvaluateCallViaArray(call, frame);
    }

    private ValueTask<SandboxValue> EvaluateFixedArityCollectionCall(
        CallExpression call,
        int arity,
        InterpreterFrame frame)
    {
        var arg0 = SandboxValue.Unit;
        var arg1 = SandboxValue.Unit;
        var arg2 = SandboxValue.Unit;
        for (var i = 0; i < arity; i++)
        {
            var argTask = EvaluateAsync(call.Arguments[i], frame);
            if (!argTask.IsCompletedSuccessfully)
            {
                return AwaitCollectionOperands(call, arity, i, argTask, arg0, arg1, frame);
            }

            switch (i)
            {
                case 0:
                    arg0 = argTask.Result;
                    break;
                case 1:
                    arg1 = argTask.Result;
                    break;
                default:
                    arg2 = argTask.Result;
                    break;
            }
        }

        return new ValueTask<SandboxValue>(
            CollectionIntrinsicDispatcher.Dispatch(call, arg0, arg1, arg2, _context));
    }

    private async ValueTask<SandboxValue> AwaitCollectionOperands(
        CallExpression call,
        int arity,
        int pending,
        ValueTask<SandboxValue> pendingTask,
        SandboxValue arg0,
        SandboxValue arg1,
        InterpreterFrame frame)
    {
        var arg2 = SandboxValue.Unit;
        var resolved = await pendingTask.ConfigureAwait(false);
        switch (pending)
        {
            case 0:
                arg0 = resolved;
                break;
            case 1:
                arg1 = resolved;
                break;
            default:
                arg2 = resolved;
                break;
        }

        for (var i = pending + 1; i < arity; i++)
        {
            var operand = await EvaluateAsync(call.Arguments[i], frame).ConfigureAwait(false);
            switch (i)
            {
                case 1:
                    arg1 = operand;
                    break;
                default:
                    arg2 = operand;
                    break;
            }
        }

        return CollectionIntrinsicDispatcher.Dispatch(call, arg0, arg1, arg2, _context);
    }

    private ValueTask<SandboxValue> EvaluateCallViaArray(CallExpression call, InterpreterFrame frame)
    {
        var arguments = call.Arguments;
        var argCount = arguments.Count;
        var args = argCount == 0 ? Array.Empty<SandboxValue>() : new SandboxValue[argCount];
        for (var i = 0; i < argCount; i++)
        {
            var argTask = EvaluateAsync(arguments[i], frame);
            if (!argTask.IsCompletedSuccessfully)
            {
                return AwaitCallArguments(call, args, i, argTask, frame);
            }

            args[i] = argTask.Result;
        }

        return DispatchCall(call, args, frame);
    }

    private async ValueTask<SandboxValue> AwaitCallArguments(
        CallExpression call,
        SandboxValue[] args,
        int pending,
        ValueTask<SandboxValue> pendingTask,
        InterpreterFrame frame)
    {
        args[pending] = await pendingTask.ConfigureAwait(false);
        for (var i = pending + 1; i < args.Length; i++)
        {
            args[i] = await EvaluateAsync(call.Arguments[i], frame).ConfigureAwait(false);
        }

        return await DispatchCall(call, args, frame).ConfigureAwait(false);
    }

    private ValueTask<SandboxValue> DispatchCall(CallExpression call, SandboxValue[] args, InterpreterFrame frame)
    {
        if (TryEvaluateCollectionCall(call, args, out var collectionValue))
        {
            return new ValueTask<SandboxValue>(collectionValue);
        }

        if (_interpreter.TryGetFunction(call.Name, out var function))
        {
            return _interpreter.InvokeFunctionAsync(function, args);
        }

        if (_context.Bindings.TryGetDescriptor(call.Name, out var descriptor))
        {
            return InterpreterBindingCaller.CallAsync(
                _context, _options, _moduleHash, descriptor, args, frame.FunctionId);
        }

        throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, $"unknown call '{call.Name}' at runtime"));
    }

    private bool TryEvaluateCollectionCall(
        CallExpression call,
        IReadOnlyList<SandboxValue> args,
        out SandboxValue value)
    {
        value = call.Name switch
        {
            "list.empty" => CollectionOperations.CreateList(call.GenericType ?? SandboxType.Unit, _context),
            "list.of" => CollectionOperations.BuildList(args, _context),
            "list.count" => CollectionOperations.CountList(Arg(args, 0), _context),
            "list.get" => CollectionOperations.GetListItem(Arg(args, 1), Arg(args, 0), _context),
            "list.add" => CollectionOperations.AddListItem(Arg(args, 1), Arg(args, 0), _context),
            "map.empty" => CollectionOperations.CreateMap(
                call.GenericType ?? SandboxType.Map(SandboxType.Unit, SandboxType.Unit),
                _context),
            "map.containsKey" => CollectionOperations.ContainsMapKey(Arg(args, 1), Arg(args, 0), _context),
            "map.get" => CollectionOperations.GetMapValue(Arg(args, 1), Arg(args, 0), _context),
            "map.set" => CollectionOperations.SetMapValue(Arg(args, 2), Arg(args, 1), Arg(args, 0), _context),
            "map.remove" => CollectionOperations.RemoveMapValue(Arg(args, 1), Arg(args, 0), _context),
            "record.new" => CollectionOperations.BuildRecord(args, _context),
            "record.get" => CollectionOperations.GetRecordField(Arg(args, 1), Arg(args, 0), _context),
            "numeric.toI64" => NumericToInt64(Arg(args, 0)),
            "numeric.toF64" => NumericToDouble(Arg(args, 0)),
            _ => SandboxValue.Unit
        };
        return call.Name is "list.empty" or "list.of" or "list.count" or "list.get" or "list.add"
            or "map.empty" or "map.containsKey" or "map.get" or "map.set" or "map.remove"
            or "record.new" or "record.get"
            or "numeric.toI64" or "numeric.toF64";
    }

    private static SandboxValue Arg(IReadOnlyList<SandboxValue> args, int index)
        => index < args.Count
            ? args[index]
            : throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "call arity mismatch"));

    private static SandboxValue NumericToInt64(SandboxValue value)
        => value switch
        {
            I32Value number => SandboxValue.FromInt64(number.Value),
            _ => throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "expected I32 value"))
        };

    private static SandboxValue NumericToDouble(SandboxValue value)
        => value switch
        {
            I32Value number => SandboxValue.FromDouble(number.Value),
            I64Value number => SandboxValue.FromDouble(number.Value),
            _ => throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "expected I32 or I64 value"))
        };
}
