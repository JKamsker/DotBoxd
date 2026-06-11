namespace SafeIR.Interpreter;

using SafeIR;

internal sealed class ExpressionEvaluator
{
    private readonly SandboxContext _context;
    private readonly InterpreterEvaluator _interpreter;

    public ExpressionEvaluator(SandboxContext context, InterpreterEvaluator interpreter)
    {
        _context = context;
        _interpreter = interpreter;
    }

    public async ValueTask<SandboxValue> EvaluateAsync(Expression expression, InterpreterFrame frame)
    {
        _context.ChargeFuel(1);
        return expression switch {
            LiteralExpression literal => literal.Value,
            VariableExpression variable => frame.Locals[variable.Name],
            UnaryExpression unary => await EvaluateUnaryAsync(unary, frame).ConfigureAwait(false),
            BinaryExpression binary => await EvaluateBinaryAsync(binary, frame).ConfigureAwait(false),
            CallExpression call => await EvaluateCallAsync(call, frame).ConfigureAwait(false),
            _ => throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "unsupported expression"))
        };
    }

    private async ValueTask<SandboxValue> EvaluateUnaryAsync(UnaryExpression unary, InterpreterFrame frame)
    {
        var value = await EvaluateAsync(unary.Operand, frame).ConfigureAwait(false);
        return unary.Operator switch {
            "!" => SandboxValue.FromBool(!((BoolValue)value).Value),
            "-" => SandboxValue.FromInt32(-((I32Value)value).Value),
            _ => throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "unsupported unary operator"))
        };
    }

    private async ValueTask<SandboxValue> EvaluateBinaryAsync(BinaryExpression binary, InterpreterFrame frame)
    {
        var left = await EvaluateAsync(binary.Left, frame).ConfigureAwait(false);
        var right = await EvaluateAsync(binary.Right, frame).ConfigureAwait(false);
        return binary.Operator switch {
            "+" when left is StringValue l && right is StringValue r => Concat(l.Value, r.Value),
            "+" => SandboxValue.FromInt32(((I32Value)left).Value + ((I32Value)right).Value),
            "-" => SandboxValue.FromInt32(((I32Value)left).Value - ((I32Value)right).Value),
            "*" => SandboxValue.FromInt32(((I32Value)left).Value * ((I32Value)right).Value),
            "/" => SandboxValue.FromInt32(((I32Value)left).Value / ((I32Value)right).Value),
            "%" => SandboxValue.FromInt32(((I32Value)left).Value % ((I32Value)right).Value),
            "==" => SandboxValue.FromBool(Equals(left, right)),
            "!=" => SandboxValue.FromBool(!Equals(left, right)),
            "<" => SandboxValue.FromBool(((I32Value)left).Value < ((I32Value)right).Value),
            "<=" => SandboxValue.FromBool(((I32Value)left).Value <= ((I32Value)right).Value),
            ">" => SandboxValue.FromBool(((I32Value)left).Value > ((I32Value)right).Value),
            ">=" => SandboxValue.FromBool(((I32Value)left).Value >= ((I32Value)right).Value),
            "&&" => SandboxValue.FromBool(((BoolValue)left).Value && ((BoolValue)right).Value),
            "||" => SandboxValue.FromBool(((BoolValue)left).Value || ((BoolValue)right).Value),
            _ => throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "unsupported binary operator"))
        };
    }

    private async ValueTask<SandboxValue> EvaluateCallAsync(CallExpression call, InterpreterFrame frame)
    {
        var args = new List<SandboxValue>(call.Arguments.Count);
        foreach (var arg in call.Arguments) {
            args.Add(await EvaluateAsync(arg, frame).ConfigureAwait(false));
        }

        if (TryEvaluateCollectionCall(call, args, out var collectionValue)) {
            return collectionValue;
        }

        if (_context.Bindings.TryGet(call.Name, out _)) {
            return await CallBindingAsync(call.Name, args).ConfigureAwait(false);
        }

        if (_interpreter.TryGetFunction(call.Name, out var function)) {
            return await _interpreter.InvokeFunctionAsync(function, args).ConfigureAwait(false);
        }

        throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, $"unknown call '{call.Name}' at runtime"));
    }

    private bool TryEvaluateCollectionCall(
        CallExpression call,
        IReadOnlyList<SandboxValue> args,
        out SandboxValue value)
    {
        value = call.Name switch {
            "list.empty" => CollectionOperations.CreateList(call.GenericType ?? SandboxType.Unit, _context),
            "list.of" => CollectionOperations.BuildList(args, _context),
            "list.count" => CollectionOperations.CountList(Arg(args, 0)),
            "list.get" => CollectionOperations.GetListItem(Arg(args, 1), Arg(args, 0)),
            "list.add" => CollectionOperations.AddListItem(Arg(args, 1), Arg(args, 0), _context),
            "map.empty" => CollectionOperations.CreateMap(
                call.GenericType ?? SandboxType.Map(SandboxType.Unit, SandboxType.Unit),
                _context),
            "map.containsKey" => CollectionOperations.ContainsMapKey(Arg(args, 1), Arg(args, 0)),
            "map.get" => CollectionOperations.GetMapValue(Arg(args, 1), Arg(args, 0)),
            "map.set" => CollectionOperations.SetMapValue(Arg(args, 2), Arg(args, 1), Arg(args, 0), _context),
            "map.remove" => CollectionOperations.RemoveMapValue(Arg(args, 1), Arg(args, 0), _context),
            _ => SandboxValue.Unit
        };
        return call.Name is "list.empty" or "list.of" or "list.count" or "list.get" or "list.add"
            or "map.empty" or "map.containsKey" or "map.get" or "map.set" or "map.remove";
    }

    public async ValueTask<SandboxValue> InvokeFunctionAsync(SandboxFunction function, IReadOnlyList<SandboxValue> args)
        => await _interpreter.InvokeFunctionAsync(function, args).ConfigureAwait(false);

    private async ValueTask<SandboxValue> CallBindingAsync(string id, IReadOnlyList<SandboxValue> args)
    {
        var descriptor = _context.Bindings.GetDescriptor(id);
        if (descriptor.RequiredCapability is not null) {
            _context.RequireCapability(descriptor.RequiredCapability);
        }

        _context.Budget.ChargeHostCall(id);
        _context.ChargeFuel(descriptor.CostModel.BaseFuel);
        try {
            return await descriptor.Interpreter(_context, args, _context.CancellationToken).ConfigureAwait(false);
        }
        catch (SandboxRuntimeException) {
            throw;
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception) {
            throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.BindingFailure, $"binding '{id}' failed"));
        }
    }

    private SandboxValue Concat(string left, string right)
    {
        var text = left + right;
        _context.ChargeAllocation(text.Length * sizeof(char));
        return SandboxValue.FromString(text);
    }

    private static SandboxValue Arg(IReadOnlyList<SandboxValue> args, int index)
        => index < args.Count
            ? args[index]
            : throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "call arity mismatch"));
}
