namespace DotBoxd.Kernels.Interpreter;

using DotBoxd.Kernels;

internal sealed class InterpreterEvaluator : I32CallEvaluator
{
    private readonly SandboxContext _context;
    private readonly IReadOnlyDictionary<string, SandboxFunction> _functions;
    private readonly IReadOnlyDictionary<string, FunctionAnalysis> _functionAnalysis;
    private readonly SandboxExecutionOptions _options;
    private readonly string _moduleHash;
    private readonly ExpressionEvaluator _expressions;
    private readonly StatementExecutor _statements;
    private readonly Dictionary<string, FunctionFrameLayout> _frameLayouts = new(StringComparer.Ordinal);

    public InterpreterEvaluator(ExecutionPlan plan, SandboxContext context, SandboxExecutionOptions options)
    {
        _context = context;
        _options = options;
        _moduleHash = plan.ModuleHash;
        _functions = plan.FunctionLookup;
        _functionAnalysis = plan.FunctionAnalysis;
        _expressions = new ExpressionEvaluator(_context, this, _functionAnalysis, _options, _moduleHash);
        _statements = new StatementExecutor(_context, _expressions, this, _options, _moduleHash);
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

    public bool CanEvaluateInt32Call(CallExpression call)
        => call.Arguments.Count == 0 &&
           _functions.TryGetValue(call.Name, out var function) &&
           function.Parameters.Count == 0 &&
           function.ReturnType == SandboxType.I32 &&
           TryGetConstantInt32Return(function, out _);

    public int EvaluateInt32Call(CallExpression call)
    {
        if (!_functions.TryGetValue(call.Name, out var function) ||
            !TryGetConstantInt32Return(function, out var expression))
        {
            throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, $"unknown call '{call.Name}' at runtime"));
        }

        _context.EnterCall();
        try
        {
            _context.ChargeFuel(1);
            _context.ChargeFuel(1);
            return I32ExpressionEvaluator.Evaluate(expression, frame: null, _context, calls: null);
        }
        finally
        {
            _context.ExitCall();
        }
    }

    public bool TryCreateInt32CallPlan(
        CallExpression call,
        InterpreterFrame frame,
        string assumedInt32Local,
        out I32ExpressionPlan plan)
    {
        plan = null!;
        if (!_functions.TryGetValue(call.Name, out var function) ||
            !TryGetInlineableInt32Return(function, call, out var expression))
        {
            return false;
        }

        var parameter = function.Parameters[0];
        if (!I32ExpressionPlan.TryCreate(call.Arguments[0], frame, assumedInt32Local, this, out var argument))
        {
            return false;
        }

        var substitutions = new Dictionary<string, I32ExpressionPlan>(StringComparer.Ordinal) {
            [parameter.Name] = argument
        };
        if (!I32ExpressionPlan.TryCreate(
            expression,
            frame,
            assumedInt32Local,
            calls: null,
            substitutions,
            out var body))
        {
            return false;
        }

        plan = I32ExpressionPlan.InlineCall(body);
        return true;
    }

    // Non-async invocation: a function whose body is fully synchronous (no pending
    // host binding) completes without ever allocating an async state machine, so a
    // helper called inside a loop costs only its indexed frame object per call.
    public ValueTask<SandboxValue> InvokeFunctionAsync(SandboxFunction function, IReadOnlyList<SandboxValue> args)
    {
        _context.EnterCall();
        var exited = false;
        try
        {
            _context.ChargeFuel(1);
            var frame = InterpreterFrame.Create(GetFrameLayout(function), function, args);
            var body = function.Body;
            for (var i = 0; i < body.Count; i++)
            {
                var statementTask = _statements.ExecuteStatementAsync(body[i], frame);
                if (!statementTask.IsCompletedSuccessfully)
                {
                    exited = true;
                    return AwaitInvoke(function, statementTask, frame, i + 1);
                }

                var result = statementTask.Result;
                if (result is not null)
                {
                    EntrypointBinder.RequireType(result, function.ReturnType, "function return type mismatch");
                    return new ValueTask<SandboxValue>(result);
                }
            }

            throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, $"function '{function.Id}' returned no value"));
        }
        finally
        {
            if (!exited)
            {
                _context.ExitCall();
            }
        }
    }

    private async ValueTask<SandboxValue> AwaitInvoke(
        SandboxFunction function,
        ValueTask<SandboxValue?> pendingTask,
        InterpreterFrame frame,
        int nextStatement)
    {
        try
        {
            var result = await pendingTask.ConfigureAwait(false);
            if (result is not null)
            {
                EntrypointBinder.RequireType(result, function.ReturnType, "function return type mismatch");
                return result;
            }

            var body = function.Body;
            for (var i = nextStatement; i < body.Count; i++)
            {
                result = await _statements.ExecuteStatementAsync(body[i], frame).ConfigureAwait(false);
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

    // The function set is fixed for the lifetime of an evaluator, so each function's
    // local slot layout is resolved once and reused across every invocation instead
    // of rebuilding a string-keyed local map per call.
    private FunctionFrameLayout GetFrameLayout(SandboxFunction function)
    {
        if (!_frameLayouts.TryGetValue(function.Id, out var layout))
        {
            layout = FunctionFrameLayout.Build(function, _functionAnalysis, _context.Bindings);
            _frameLayouts[function.Id] = layout;
        }

        return layout;
    }

    private static bool TryGetConstantInt32Return(SandboxFunction function, out Expression expression)
    {
        if (function.Body.Count == 1 &&
            function.Body[0] is ReturnStatement ret &&
            I32ExpressionEvaluator.CanEvaluate(ret.Value, frame: null))
        {
            expression = ret.Value;
            return true;
        }

        expression = null!;
        return false;
    }

    private static bool TryGetInlineableInt32Return(
        SandboxFunction function,
        CallExpression call,
        out Expression expression)
    {
        if (call.Arguments.Count == 1 &&
            IsSimpleInlineArgument(call.Arguments[0]) &&
            function.Parameters.Count == 1 &&
            function.Parameters[0].Type == SandboxType.I32 &&
            function.ReturnType == SandboxType.I32 &&
            function.Body.Count == 1 &&
            function.Body[0] is ReturnStatement ret &&
            !ContainsCall(ret.Value) &&
            CountVariableUses(ret.Value, function.Parameters[0].Name) == 1)
        {
            expression = ret.Value;
            return true;
        }

        expression = null!;
        return false;
    }

    private static int CountVariableUses(Expression expression, string name)
        => expression switch {
            VariableExpression variable => string.Equals(variable.Name, name, StringComparison.Ordinal) ? 1 : 0,
            UnaryExpression unary => CountVariableUses(unary.Operand, name),
            BinaryExpression binary => CountVariableUses(binary.Left, name) + CountVariableUses(binary.Right, name),
            CallExpression call => call.Arguments.Sum(argument => CountVariableUses(argument, name)),
            _ => 0
        };

    private static bool IsSimpleInlineArgument(Expression expression)
        => expression is LiteralExpression { Value: I32Value } or VariableExpression;

    private static bool ContainsCall(Expression expression)
        => expression switch {
            CallExpression => true,
            UnaryExpression unary => ContainsCall(unary.Operand),
            BinaryExpression binary => ContainsCall(binary.Left) || ContainsCall(binary.Right),
            _ => false
        };
}
