namespace DotBoxd.Kernels.Interpreter;

using DotBoxd.Kernels;

internal sealed class ExpressionEvaluator
{
    private readonly SandboxContext _context;
    private readonly InterpreterEvaluator _interpreter;
    private readonly IReadOnlyDictionary<string, FunctionAnalysis> _functionAnalysis;
    private readonly SandboxExecutionOptions _options;
    private readonly string _moduleHash;

    public ExpressionEvaluator(
        SandboxContext context,
        InterpreterEvaluator interpreter,
        IReadOnlyDictionary<string, FunctionAnalysis> functionAnalysis,
        SandboxExecutionOptions options,
        string moduleHash)
    {
        _context = context;
        _interpreter = interpreter;
        _functionAnalysis = functionAnalysis;
        _options = options;
        _moduleHash = moduleHash;
    }

    // Non-async dispatch: literals, variables, arithmetic, and pure helper calls all
    // complete synchronously, so they return a finished ValueTask without ever
    // allocating an async state machine. Only genuinely asynchronous work (a host
    // binding whose ValueTask is still pending) walks the async continuation path.
    public ValueTask<SandboxValue> EvaluateAsync(Expression expression, InterpreterFrame frame)
    {
        if (!_options.EnableDebugTrace &&
            I32ExpressionEvaluator.CanEvaluate(expression, frame, _interpreter))
        {
            return new ValueTask<SandboxValue>(
                SandboxValue.FromInt32(I32ExpressionEvaluator.Evaluate(expression, frame, _context, _interpreter)));
        }

        _context.ChargeFuel(1);
        InterpreterTrace.Write(
            _context,
            _options,
            _moduleHash,
            frame.FunctionId,
            "expression",
            expression.GetType().Name,
            expression.Span);
        return expression switch
        {
            LiteralExpression literal => new ValueTask<SandboxValue>(ChargeLiteral(literal.Value)),
            VariableExpression variable => new ValueTask<SandboxValue>(frame.Read(variable.Name)),
            UnaryExpression unary => EvaluateUnary(unary, frame),
            BinaryExpression binary => EvaluateBinary(binary, frame),
            CallExpression call => EvaluateCall(call, frame),
            _ => throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "unsupported expression"))
        };
    }

    public bool TryEvaluateInt32(Expression expression, InterpreterFrame frame, out int value)
    {
        if (!_options.EnableDebugTrace && I32ExpressionEvaluator.CanEvaluate(expression, frame, _interpreter))
        {
            value = I32ExpressionEvaluator.Evaluate(expression, frame, _context, _interpreter);
            return true;
        }

        value = 0;
        return false;
    }

    private ValueTask<SandboxValue> EvaluateUnary(UnaryExpression unary, InterpreterFrame frame)
    {
        var operand = EvaluateAsync(unary.Operand, frame);
        return operand.IsCompletedSuccessfully
            ? new ValueTask<SandboxValue>(OperatorEvaluator.ApplyUnary(unary, operand.Result))
            : AwaitUnary(unary, operand);
    }

    private async ValueTask<SandboxValue> AwaitUnary(UnaryExpression unary, ValueTask<SandboxValue> operand)
        => OperatorEvaluator.ApplyUnary(unary, await operand.ConfigureAwait(false));

    private ValueTask<SandboxValue> EvaluateBinary(BinaryExpression binary, InterpreterFrame frame)
    {
        if (binary.Operator is "&&" or "||")
        {
            return EvaluateShortCircuit(binary, frame);
        }

        var leftTask = EvaluateAsync(binary.Left, frame);
        if (!leftTask.IsCompletedSuccessfully)
        {
            return AwaitBinary(binary, leftTask, frame);
        }

        var rightTask = EvaluateAsync(binary.Right, frame);
        if (!rightTask.IsCompletedSuccessfully)
        {
            return AwaitBinaryRight(binary, leftTask.Result, rightTask);
        }

        return new ValueTask<SandboxValue>(OperatorEvaluator.ApplyBinary(binary, leftTask.Result, rightTask.Result, _context));
    }

    private async ValueTask<SandboxValue> AwaitBinary(
        BinaryExpression binary,
        ValueTask<SandboxValue> leftTask,
        InterpreterFrame frame)
    {
        var left = await leftTask.ConfigureAwait(false);
        var right = await EvaluateAsync(binary.Right, frame).ConfigureAwait(false);
        return OperatorEvaluator.ApplyBinary(binary, left, right, _context);
    }

    private async ValueTask<SandboxValue> AwaitBinaryRight(
        BinaryExpression binary,
        SandboxValue left,
        ValueTask<SandboxValue> rightTask)
        => OperatorEvaluator.ApplyBinary(binary, left, await rightTask.ConfigureAwait(false), _context);

    private ValueTask<SandboxValue> EvaluateShortCircuit(BinaryExpression binary, InterpreterFrame frame)
    {
        var shortCircuitOn = binary.Operator == "||";
        var order = ShortCircuitExpressionOrder.Choose(binary, _context.Bindings, _functionAnalysis);
        var firstTask = EvaluateAsync(order.First, frame);
        if (!firstTask.IsCompletedSuccessfully)
        {
            return AwaitShortCircuit(order, shortCircuitOn, firstTask, frame);
        }

        if (((BoolValue)firstTask.Result).Value == shortCircuitOn)
        {
            return new ValueTask<SandboxValue>(SandboxValue.FromBool(shortCircuitOn));
        }

        var secondTask = EvaluateAsync(order.Second, frame);
        return secondTask.IsCompletedSuccessfully
            ? new ValueTask<SandboxValue>(SandboxValue.FromBool(((BoolValue)secondTask.Result).Value))
            : AwaitShortCircuitSecond(secondTask);
    }

    private async ValueTask<SandboxValue> AwaitShortCircuit(
        ShortCircuitOperands order,
        bool shortCircuitOn,
        ValueTask<SandboxValue> firstTask,
        InterpreterFrame frame)
    {
        var first = (BoolValue)await firstTask.ConfigureAwait(false);
        if (first.Value == shortCircuitOn)
        {
            return SandboxValue.FromBool(shortCircuitOn);
        }

        var second = (BoolValue)await EvaluateAsync(order.Second, frame).ConfigureAwait(false);
        return SandboxValue.FromBool(second.Value);
    }

    private static async ValueTask<SandboxValue> AwaitShortCircuitSecond(ValueTask<SandboxValue> secondTask)
        => SandboxValue.FromBool(((BoolValue)await secondTask.ConfigureAwait(false)).Value);

    private ValueTask<SandboxValue> EvaluateCall(CallExpression call, InterpreterFrame frame)
    {
        if (UnaryPureIntrinsicDispatcher.IsCandidate(call.Name) &&
            UnaryPureIntrinsicDispatcher.TryEvaluate(
                call, this, frame, _context, _options, _moduleHash, frame.FunctionId, out var mathValue))
        {
            return mathValue;
        }

        // Fixed-arity collection intrinsics complete synchronously and never let an
        // argument escape, so they can be dispatched straight from locals without
        // allocating a per-call argument array. Operands are evaluated in source order;
        // a pending operand continues on the array-free async path below rather than
        // falling back to the array path, so no already-evaluated operand is re-run.
        // Variadic list.of, local functions, and host bindings still use the array path
        // so callees that may retain the list see a stable, exact-length sequence.
        var fixedArity = CollectionIntrinsicDispatcher.FixedArity(call.Name);
        if (fixedArity >= 0 && fixedArity == call.Arguments.Count)
        {
            return EvaluateFixedArityCollectionCall(call, fixedArity, frame);
        }

        return EvaluateCallViaArray(call, frame);
    }

    // Evaluates up to three operands for a fixed-arity collection intrinsic and
    // dispatches without an argument array. Each operand is evaluated exactly once: if
    // one is still pending, the already-evaluated operands are carried into an async
    // continuation instead of being recomputed.
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
                case 0: arg0 = argTask.Result; break;
                case 1: arg1 = argTask.Result; break;
                default: arg2 = argTask.Result; break;
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
            case 0: arg0 = resolved; break;
            case 1: arg1 = resolved; break;
            default: arg2 = resolved; break;
        }

        for (var i = pending + 1; i < arity; i++)
        {
            var operand = await EvaluateAsync(call.Arguments[i], frame).ConfigureAwait(false);
            switch (i)
            {
                case 1: arg1 = operand; break;
                default: arg2 = operand; break;
            }
        }

        return CollectionIntrinsicDispatcher.Dispatch(call, arg0, arg1, arg2, _context);
    }

    private ValueTask<SandboxValue> EvaluateCallViaArray(CallExpression call, InterpreterFrame frame)
    {
        var arguments = call.Arguments;
        var argCount = arguments.Count;
        // Evaluated arguments are passed to callees as a fixed read-only list, so
        // size the backing array exactly and reuse the shared empty array for the
        // common zero-argument call instead of allocating a growable List per call.
        var args = argCount == 0 ? System.Array.Empty<SandboxValue>() : new SandboxValue[argCount];
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

        if (_context.Bindings.Contains(call.Name))
        {
            return InterpreterBindingCaller.CallAsync(
                _context, _options, _moduleHash, call.Name, args, frame.FunctionId);
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
            _ => SandboxValue.Unit
        };
        return call.Name is "list.empty" or "list.of" or "list.count" or "list.get" or "list.add"
            or "map.empty" or "map.containsKey" or "map.get" or "map.set" or "map.remove"
            or "record.new" or "record.get";
    }

    public ValueTask<SandboxValue> InvokeFunctionAsync(SandboxFunction function, IReadOnlyList<SandboxValue> args)
        => _interpreter.InvokeFunctionAsync(function, args);

    private SandboxValue ChargeLiteral(SandboxValue value)
    {
        _context.ChargeValue(value);
        return value;
    }

    private static SandboxValue Arg(IReadOnlyList<SandboxValue> args, int index)
        => index < args.Count
            ? args[index]
            : throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "call arity mismatch"));
}
