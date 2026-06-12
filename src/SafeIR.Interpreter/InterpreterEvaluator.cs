namespace SafeIR.Interpreter;

using SafeIR;

internal sealed class InterpreterEvaluator
{
    private readonly SandboxContext _context;
    private readonly Dictionary<string, SandboxFunction> _functions;
    private readonly IReadOnlyDictionary<string, FunctionAnalysis> _functionAnalysis;
    private readonly SandboxExecutionOptions _options;
    private readonly string _moduleHash;

    public InterpreterEvaluator(ExecutionPlan plan, SandboxContext context, SandboxExecutionOptions options)
    {
        _context = context;
        _options = options;
        _moduleHash = plan.ModuleHash;
        _functions = plan.Module.Functions.ToDictionary(f => f.Id, StringComparer.Ordinal);
        _functionAnalysis = plan.FunctionAnalysis;
    }

    public ValueTask<SandboxValue> ExecuteEntrypointAsync(string entrypoint, SandboxValue input)
    {
        if (!_functions.TryGetValue(entrypoint, out var function) || !function.IsEntrypoint)
        {
            throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, $"entrypoint '{entrypoint}' is not available"));
        }

        _context.ChargeValue(input);
        return InvokeFunctionAsync(function, EntrypointBinder.BindArguments(function, input));
    }

    public bool TryGetFunction(string id, out SandboxFunction function) => _functions.TryGetValue(id, out function!);

    public async ValueTask<SandboxValue> InvokeFunctionAsync(SandboxFunction function, IReadOnlyList<SandboxValue> args)
    {
        _context.EnterCall();
        try
        {
            _context.ChargeFuel(1);
            var frame = InterpreterFrame.Create(function, args);
            foreach (var statement in function.Body)
            {
                var result = await ExecuteStatementAsync(statement, frame).ConfigureAwait(false);
                if (result is not null)
                {
                    EntrypointBinder.RequireType(result, function.ReturnType, "function return type mismatch");
                    return result;
                }
            }

            throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, $"function '{function.Id}' returned no value"));
        }
        finally
        {
            _context.ExitCall();
        }
    }

    private async ValueTask<SandboxValue?> ExecuteStatementAsync(Statement statement, InterpreterFrame frame)
    {
        _context.ChargeFuel(1);
        InterpreterTrace.Write(_context, _options, _moduleHash, frame.FunctionId, "statement", statement.GetType().Name);
        switch (statement)
        {
            case AssignmentStatement assignment:
                frame.Locals[assignment.Name] = await EvaluateAsync(assignment.Value, frame).ConfigureAwait(false);
                return null;
            case ReturnStatement ret:
                return await EvaluateAsync(ret.Value, frame).ConfigureAwait(false);
            case ExpressionStatement expression:
                _ = await EvaluateAsync(expression.Value, frame).ConfigureAwait(false);
                return null;
            case IfStatement branch:
                return await ExecuteIfAsync(branch, frame).ConfigureAwait(false);
            case WhileStatement loop:
                return await ExecuteWhileAsync(loop, frame).ConfigureAwait(false);
            case ForRangeStatement range:
                return await ExecuteForAsync(range, frame).ConfigureAwait(false);
            default:
                throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "unsupported statement"));
        }
    }

    private async ValueTask<SandboxValue?> ExecuteIfAsync(IfStatement statement, InterpreterFrame frame)
    {
        var condition = (BoolValue)await EvaluateAsync(statement.Condition, frame).ConfigureAwait(false);
        return await ExecuteBlockAsync(condition.Value ? statement.Then : statement.Else, frame).ConfigureAwait(false);
    }

    private async ValueTask<SandboxValue?> ExecuteWhileAsync(WhileStatement statement, InterpreterFrame frame)
    {
        while (((BoolValue)await EvaluateAsync(statement.Condition, frame).ConfigureAwait(false)).Value)
        {
            _context.ChargeFuel(5);
            var value = await ExecuteBlockAsync(statement.Body, frame).ConfigureAwait(false);
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private async ValueTask<SandboxValue?> ExecuteForAsync(ForRangeStatement statement, InterpreterFrame frame)
    {
        var start = ((I32Value)await EvaluateAsync(statement.Start, frame).ConfigureAwait(false)).Value;
        var end = ((I32Value)await EvaluateAsync(statement.End, frame).ConfigureAwait(false)).Value;
        for (var i = start; i < end; i++)
        {
            _context.ChargeFuel(5);
            frame.Locals[statement.LocalName] = SandboxValue.FromInt32(i);
            var value = await ExecuteBlockAsync(statement.Body, frame).ConfigureAwait(false);
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private async ValueTask<SandboxValue?> ExecuteBlockAsync(IReadOnlyList<Statement> statements, InterpreterFrame frame)
    {
        foreach (var statement in statements)
        {
            var value = await ExecuteStatementAsync(statement, frame).ConfigureAwait(false);
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private ValueTask<SandboxValue> EvaluateAsync(Expression expression, InterpreterFrame frame)
        => new ExpressionEvaluator(_context, this, _functionAnalysis, _options, _moduleHash).EvaluateAsync(expression, frame);
}
