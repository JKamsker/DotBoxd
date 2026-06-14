namespace DotBoxD.Kernels.Interpreter.Internal;

using DotBoxD.Kernels;

/// <summary>
/// Walks and executes statements, blocks, and loops for a single interpreter.
/// Mirrors <see cref="ExpressionEvaluator"/>: each entry point stays on a
/// non-async fast path whenever the underlying expression completes synchronously,
/// allocating an async state machine only when a host binding is still pending.
/// </summary>
internal sealed class StatementExecutor
{
    private readonly SandboxContext _context;
    private readonly ExpressionEvaluator _expressions;
    private readonly I32CallEvaluator _calls;
    private readonly SandboxExecutionOptions _options;
    private readonly string _moduleHash;

    public StatementExecutor(
        SandboxContext context,
        ExpressionEvaluator expressions,
        I32CallEvaluator calls,
        SandboxExecutionOptions options,
        string moduleHash)
    {
        _context = context;
        _expressions = expressions;
        _calls = calls;
        _options = options;
        _moduleHash = moduleHash;
    }

    public ValueTask<SandboxValue?> ExecuteStatementAsync(Statement statement, InterpreterFrame frame)
    {
        _context.ChargeFuel(1);
        InterpreterTrace.Write(
            _context,
            _options,
            _moduleHash,
            frame.FunctionId,
            "statement",
            statement.GetType().Name,
            statement.Span);
        switch (statement)
        {
            case AssignmentStatement assignment:
                return ExecuteAssignment(assignment, frame);
            case ReturnStatement ret:
                return AsNullable(EvaluateAsync(ret.Value, frame));
            case ExpressionStatement expression:
                return DiscardResult(EvaluateAsync(expression.Value, frame));
            case IfStatement branch:
                return ExecuteIfAsync(branch, frame);
            case WhileStatement loop:
                return ExecuteWhileAsync(loop, frame);
            case ForRangeStatement range:
                return ExecuteForAsync(range, frame);
            default:
                throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "unsupported statement"));
        }
    }

    public ValueTask<SandboxValue?> ExecuteBlockAsync(IReadOnlyList<Statement> statements, InterpreterFrame frame)
    {
        for (var i = 0; i < statements.Count; i++)
        {
            var statementTask = ExecuteStatementAsync(statements[i], frame);
            if (!statementTask.IsCompletedSuccessfully)
            {
                return AwaitBlock(statements, statementTask, i + 1, frame);
            }

            var value = statementTask.Result;
            if (value is not null)
            {
                return new ValueTask<SandboxValue?>(value);
            }
        }

        return default;
    }

    private ValueTask<SandboxValue?> ExecuteAssignment(AssignmentStatement assignment, InterpreterFrame frame)
    {
        if (_expressions.TryEvaluateInt32(assignment.Value, frame, out var i32Value))
        {
            frame.WriteInt32(assignment.Name, i32Value);
            return default;
        }

        var valueTask = EvaluateAsync(assignment.Value, frame);
        if (valueTask.IsCompletedSuccessfully)
        {
            frame.Write(assignment.Name, valueTask.Result);
            return default;
        }

        return AwaitAssignment(assignment, valueTask, frame);
    }

    private async ValueTask<SandboxValue?> AwaitAssignment(
        AssignmentStatement assignment,
        ValueTask<SandboxValue> valueTask,
        InterpreterFrame frame)
    {
        frame.Write(assignment.Name, await valueTask.ConfigureAwait(false));
        return null;
    }

    private static ValueTask<SandboxValue?> AsNullable(ValueTask<SandboxValue> task)
        => task.IsCompletedSuccessfully
            ? new ValueTask<SandboxValue?>(task.Result)
            : AwaitNullable(task);

    private static async ValueTask<SandboxValue?> AwaitNullable(ValueTask<SandboxValue> task)
        => await task.ConfigureAwait(false);

    private static ValueTask<SandboxValue?> DiscardResult(ValueTask<SandboxValue> task)
    {
        if (task.IsCompletedSuccessfully)
        {
            _ = task.Result;
            return default;
        }

        return AwaitDiscard(task);
    }

    private static async ValueTask<SandboxValue?> AwaitDiscard(ValueTask<SandboxValue> task)
    {
        _ = await task.ConfigureAwait(false);
        return null;
    }

    private ValueTask<SandboxValue?> ExecuteIfAsync(IfStatement statement, InterpreterFrame frame)
    {
        var conditionTask = EvaluateAsync(statement.Condition, frame);
        if (!conditionTask.IsCompletedSuccessfully)
        {
            return AwaitIf(statement, conditionTask, frame);
        }

        var branch = ((BoolValue)conditionTask.Result).Value ? statement.Then : statement.Else;
        return ExecuteBlockAsync(branch, frame);
    }

    private async ValueTask<SandboxValue?> AwaitIf(
        IfStatement statement,
        ValueTask<SandboxValue> conditionTask,
        InterpreterFrame frame)
    {
        var condition = (BoolValue)await conditionTask.ConfigureAwait(false);
        return await ExecuteBlockAsync(condition.Value ? statement.Then : statement.Else, frame).ConfigureAwait(false);
    }

    private ValueTask<SandboxValue?> ExecuteWhileAsync(WhileStatement statement, InterpreterFrame frame)
    {
        if (WhileI32ForLoopRunner.TryRun(statement, frame, _context, _options, _calls))
        {
            return default;
        }

        return ExecuteWhileBoxedAsync(statement, frame);
    }

    private async ValueTask<SandboxValue?> ExecuteWhileBoxedAsync(WhileStatement statement, InterpreterFrame frame)
    {
        while (((BoolValue)await EvaluateAsync(statement.Condition, frame).ConfigureAwait(false)).Value)
        {
            _context.ChargeLoopIteration(5);
            var value = await ExecuteBlockAsync(statement.Body, frame).ConfigureAwait(false);
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private ValueTask<SandboxValue?> ExecuteForAsync(ForRangeStatement statement, InterpreterFrame frame)
    {
        if (_expressions.TryEvaluateInt32(statement.Start, frame, out var start))
        {
            return _expressions.TryEvaluateInt32(statement.End, frame, out var end)
                ? RunForLoop(statement, start, end, frame)
                : RunForLoopFromAsyncEnd(statement, start, frame);
        }

        var startTask = EvaluateAsync(statement.Start, frame);
        if (!startTask.IsCompletedSuccessfully)
        {
            return AwaitForBounds(statement, startTask, frame);
        }

        var endTask = EvaluateAsync(statement.End, frame);
        if (!endTask.IsCompletedSuccessfully)
        {
            return AwaitForEnd(statement, ((I32Value)startTask.Result).Value, endTask, frame);
        }

        return RunForLoop(statement, ((I32Value)startTask.Result).Value, ((I32Value)endTask.Result).Value, frame);
    }

    private async ValueTask<SandboxValue?> RunForLoopFromAsyncEnd(
        ForRangeStatement statement,
        int start,
        InterpreterFrame frame)
    {
        var end = ((I32Value)await EvaluateAsync(statement.End, frame).ConfigureAwait(false)).Value;
        return await RunForLoop(statement, start, end, frame).ConfigureAwait(false);
    }

    private async ValueTask<SandboxValue?> AwaitForBounds(
        ForRangeStatement statement,
        ValueTask<SandboxValue> startTask,
        InterpreterFrame frame)
    {
        var start = ((I32Value)await startTask.ConfigureAwait(false)).Value;
        var end = ((I32Value)await EvaluateAsync(statement.End, frame).ConfigureAwait(false)).Value;
        return await RunForLoop(statement, start, end, frame).ConfigureAwait(false);
    }

    private async ValueTask<SandboxValue?> AwaitForEnd(
        ForRangeStatement statement,
        int start,
        ValueTask<SandboxValue> endTask,
        InterpreterFrame frame)
    {
        var end = ((I32Value)await endTask.ConfigureAwait(false)).Value;
        return await RunForLoop(statement, start, end, frame).ConfigureAwait(false);
    }

    private ValueTask<SandboxValue?> RunForLoop(ForRangeStatement statement, int start, int end, InterpreterFrame frame)
    {
        if (MapGetI32ForLoopRunner.TryRun(statement, start, end, frame, _context, _options))
        {
            return default;
        }

        if (ListGetI32ForLoopRunner.TryRun(statement, start, end, frame, _context, _options))
        {
            return default;
        }

        if (ListCountForLoopRunner.TryRun(statement, start, end, frame, _context, _options))
        {
            return default;
        }

        if (StringLengthForLoopRunner.TryRun(statement, start, end, frame, _context, _options))
        {
            return default;
        }

        if (I32ForLoopRunner.TryRun(statement, start, end, frame, _context, _options, _calls))
        {
            return default;
        }

        if (BranchedI32ForLoopRunner.TryRun(statement, start, end, frame, _context, _options, _calls))
        {
            return default;
        }

        if (F64ForLoopRunner.TryRun(statement, start, end, frame, _context, _options))
        {
            return default;
        }

        if (I64ForLoopRunner.TryRun(statement, start, end, frame, _context, _options))
        {
            return default;
        }

        for (var i = start; i < end; i++)
        {
            _context.ChargeLoopIteration(5);
            frame.WriteInt32(statement.LocalName, i);
            var bodyTask = ExecuteBlockAsync(statement.Body, frame);
            if (!bodyTask.IsCompletedSuccessfully)
            {
                return AwaitForIteration(statement, bodyTask, i + 1, end, frame);
            }

            var value = bodyTask.Result;
            if (value is not null)
            {
                return new ValueTask<SandboxValue?>(value);
            }
        }

        return default;
    }

    private async ValueTask<SandboxValue?> AwaitForIteration(
        ForRangeStatement statement,
        ValueTask<SandboxValue?> pendingTask,
        int nextIndex,
        int end,
        InterpreterFrame frame)
    {
        var value = await pendingTask.ConfigureAwait(false);
        if (value is not null)
        {
            return value;
        }

        for (var i = nextIndex; i < end; i++)
        {
            _context.ChargeLoopIteration(5);
            frame.WriteInt32(statement.LocalName, i);
            value = await ExecuteBlockAsync(statement.Body, frame).ConfigureAwait(false);
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private async ValueTask<SandboxValue?> AwaitBlock(
        IReadOnlyList<Statement> statements,
        ValueTask<SandboxValue?> pendingTask,
        int nextIndex,
        InterpreterFrame frame)
    {
        var value = await pendingTask.ConfigureAwait(false);
        if (value is not null)
        {
            return value;
        }

        for (var i = nextIndex; i < statements.Count; i++)
        {
            value = await ExecuteStatementAsync(statements[i], frame).ConfigureAwait(false);
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private ValueTask<SandboxValue> EvaluateAsync(Expression expression, InterpreterFrame frame)
        => _expressions.EvaluateAsync(expression, frame);
}
