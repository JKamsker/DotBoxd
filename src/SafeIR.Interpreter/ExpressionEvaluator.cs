namespace SafeIR.Interpreter;

using SafeIR;

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

    public async ValueTask<SandboxValue> EvaluateAsync(Expression expression, InterpreterFrame frame)
    {
        _context.ChargeFuel(1);
        InterpreterTrace.Write(_context, _options, _moduleHash, frame.FunctionId, "expression", expression.GetType().Name);
        var value = expression switch
        {
            LiteralExpression literal => ChargeLiteral(literal.Value),
            VariableExpression variable => frame.Locals[variable.Name],
            UnaryExpression unary => await EvaluateUnaryAsync(unary, frame).ConfigureAwait(false),
            BinaryExpression binary => await EvaluateBinaryAsync(binary, frame).ConfigureAwait(false),
            CallExpression call => await EvaluateCallAsync(call, frame).ConfigureAwait(false),
            _ => throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "unsupported expression"))
        };
        return value;
    }

    private async ValueTask<SandboxValue> EvaluateUnaryAsync(UnaryExpression unary, InterpreterFrame frame)
    {
        var value = await EvaluateAsync(unary.Operand, frame).ConfigureAwait(false);
        return unary.Operator switch
        {
            "!" => SandboxValue.FromBool(!((BoolValue)value).Value),
            "-" => SandboxValue.FromInt32(SandboxInt32Math.Negate(((I32Value)value).Value)),
            _ => throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "unsupported unary operator"))
        };
    }

    private async ValueTask<SandboxValue> EvaluateBinaryAsync(BinaryExpression binary, InterpreterFrame frame)
    {
        if (binary.Operator == "&&")
        {
            var order = ShortCircuitExpressionOrder.Choose(binary, _context.Bindings, _functionAnalysis);
            var first = (BoolValue)await EvaluateAsync(order.First, frame).ConfigureAwait(false);
            if (!first.Value)
            {
                return SandboxValue.FromBool(false);
            }

            var second = (BoolValue)await EvaluateAsync(order.Second, frame).ConfigureAwait(false);
            return SandboxValue.FromBool(second.Value);
        }

        if (binary.Operator == "||")
        {
            var order = ShortCircuitExpressionOrder.Choose(binary, _context.Bindings, _functionAnalysis);
            var first = (BoolValue)await EvaluateAsync(order.First, frame).ConfigureAwait(false);
            if (first.Value)
            {
                return SandboxValue.FromBool(true);
            }

            var second = (BoolValue)await EvaluateAsync(order.Second, frame).ConfigureAwait(false);
            return SandboxValue.FromBool(second.Value);
        }

        var left = await EvaluateAsync(binary.Left, frame).ConfigureAwait(false);
        var right = await EvaluateAsync(binary.Right, frame).ConfigureAwait(false);
        return binary.Operator switch
        {
            "+" when left is StringValue l && right is StringValue r => Concat(l.Value, r.Value),
            "+" => SandboxValue.FromInt32(SandboxInt32Math.Add(((I32Value)left).Value, ((I32Value)right).Value)),
            "-" => SandboxValue.FromInt32(SandboxInt32Math.Subtract(((I32Value)left).Value, ((I32Value)right).Value)),
            "*" => SandboxValue.FromInt32(SandboxInt32Math.Multiply(((I32Value)left).Value, ((I32Value)right).Value)),
            "/" => SandboxValue.FromInt32(SandboxInt32Math.Divide(((I32Value)left).Value, ((I32Value)right).Value)),
            "%" => SandboxValue.FromInt32(SandboxInt32Math.Remainder(((I32Value)left).Value, ((I32Value)right).Value)),
            "==" => SandboxValue.FromBool(Equals(left, right)),
            "!=" => SandboxValue.FromBool(!Equals(left, right)),
            "<" => SandboxValue.FromBool(((I32Value)left).Value < ((I32Value)right).Value),
            "<=" => SandboxValue.FromBool(((I32Value)left).Value <= ((I32Value)right).Value),
            ">" => SandboxValue.FromBool(((I32Value)left).Value > ((I32Value)right).Value),
            ">=" => SandboxValue.FromBool(((I32Value)left).Value >= ((I32Value)right).Value),
            _ => throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "unsupported binary operator"))
        };
    }

    private async ValueTask<SandboxValue> EvaluateCallAsync(CallExpression call, InterpreterFrame frame)
    {
        var args = new List<SandboxValue>(call.Arguments.Count);
        foreach (var arg in call.Arguments)
        {
            args.Add(await EvaluateAsync(arg, frame).ConfigureAwait(false));
        }

        if (TryEvaluateCollectionCall(call, args, out var collectionValue))
        {
            return collectionValue;
        }

        if (_interpreter.TryGetFunction(call.Name, out var function))
        {
            return await _interpreter.InvokeFunctionAsync(function, args).ConfigureAwait(false);
        }

        if (_context.Bindings.TryGet(call.Name, out _))
        {
            return await CallBindingAsync(call.Name, args, frame.FunctionId).ConfigureAwait(false);
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
            _ => SandboxValue.Unit
        };
        return call.Name is "list.empty" or "list.of" or "list.count" or "list.get" or "list.add"
            or "map.empty" or "map.containsKey" or "map.get" or "map.set" or "map.remove";
    }

    public async ValueTask<SandboxValue> InvokeFunctionAsync(SandboxFunction function, IReadOnlyList<SandboxValue> args)
        => await _interpreter.InvokeFunctionAsync(function, args).ConfigureAwait(false);

    private async ValueTask<SandboxValue> CallBindingAsync(
        string id,
        IReadOnlyList<SandboxValue> args,
        string functionId)
    {
        var descriptor = _context.Bindings.GetDescriptor(id);
        InterpreterTrace.WriteBindingCall(_context, _options, _moduleHash, functionId, descriptor);
        var auditCheckpoint = _context.AuditCheckpoint();
        try
        {
            _context.ChargeBindingCall(descriptor);
        }
        catch (SandboxRuntimeException ex)
        {
            _context.EnsureRequiredBindingFailureAudit(descriptor, auditCheckpoint, ex.Error.Code);
            throw;
        }

        CancellationTokenSource? timeout = null;
        try
        {
            timeout = _context.CreateWallTimeToken();
            using var returnCredits = _context.BeginBindingReturnCreditScope();
            var value = await descriptor.Invoke(_context, args, timeout.Token).ConfigureAwait(false);
            value = _context.ChargeBindingReturn(descriptor, value);
            _context.EnsureRequiredBindingSuccessAudit(descriptor, auditCheckpoint);
            return value;
        }
        catch (SandboxRuntimeException ex)
        {
            _context.EnsureRequiredBindingFailureAudit(descriptor, auditCheckpoint, ex.Error.Code);
            throw;
        }
        catch (OperationCanceledException) when (_context.CancellationToken.IsCancellationRequested)
        {
            _context.EnsureRequiredBindingFailureAudit(descriptor, auditCheckpoint, SandboxErrorCode.Cancelled);
            throw;
        }
        catch (OperationCanceledException) when (timeout?.IsCancellationRequested == true)
        {
            var error = new SandboxError(SandboxErrorCode.Timeout, $"binding '{id}' timed out");
            _context.EnsureRequiredBindingFailureAudit(descriptor, auditCheckpoint, error.Code);
            throw new SandboxRuntimeException(error);
        }
        catch (OperationCanceledException)
        {
            var error = new SandboxError(SandboxErrorCode.BindingFailure, $"binding '{id}' failed");
            _context.EnsureRequiredBindingFailureAudit(descriptor, auditCheckpoint, error.Code);
            throw new SandboxRuntimeException(error);
        }
        catch (Exception)
        {
            var error = new SandboxError(SandboxErrorCode.BindingFailure, $"binding '{id}' failed");
            _context.EnsureRequiredBindingFailureAudit(descriptor, auditCheckpoint, error.Code);
            throw new SandboxRuntimeException(error);
        }
        finally
        {
            timeout?.Dispose();
        }
    }

    private SandboxValue Concat(string left, string right)
    {
        var text = left + right;
        _context.ChargeString(text);
        return SandboxValue.FromString(text);
    }

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
