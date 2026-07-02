namespace DotBoxD.Plugins.Runtime;

public partial class HookPipeline<TEvent, TContext>
{
    public HookPipeline<TEvent, TContext> ConfigureResultDispatch<TResult>(ResultHookDispatchOptions<TResult> options)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        lock (_gate)
        {
            _resultDispatchOptions[typeof(TResult)] = options;
        }

        return this;
    }

    /// <summary>
    /// Dispatches result hooks for <paramref name="e"/> in descending priority order and returns the first
    /// successful result, or <see langword="null"/> when none is registered or none succeeds. The host applies
    /// the returned result to its live state.
    /// </summary>
    public ValueTask<TResult?> FireResultAsync<TResult>(TEvent e, CancellationToken cancellationToken = default)
        where TResult : struct, IHookResult
        => FireResultAsync(e, ResultDispatchOptions<TResult>(), cancellationToken);

    public ValueTask<TResult?> FireResultAsync<TResult>(
        TEvent e,
        ResultHookDispatchOptions<TResult> options,
        CancellationToken cancellationToken = default)
        where TResult : struct, IHookResult
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_resultHooks.HasHandlers)
        {
            return new ValueTask<TResult?>((TResult?)null);
        }

        var rawContext = cancellationToken.CanBeCanceled
            ? new HookContext(_messages, cancellationToken)
            : _defaultRawContext;
        var context = _contextFactory.Create(rawContext);
        return _resultHooks.FireAsync(e, rawContext, context, options, cancellationToken);
    }

    internal Hooks.IResultHookRegistration<TEvent>[] ResultRegistrations()
        => _resultHooks.SnapshotRegistrations(this);

    Hooks.IResultHookRegistration<TEvent>[] IHookPipeline<TEvent>.ResultRegistrations()
        => ResultRegistrations();

    internal ValueTask<TResult?> FireResultEntryAsync<TResult>(
        Hooks.ResultHookSlot<TEvent, TContext>.Entry entry,
        TEvent e,
        ResultHookDispatchOptions<TResult>? options,
        CancellationToken cancellationToken = default)
        where TResult : struct, IHookResult
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rawContext = cancellationToken.CanBeCanceled
            ? new HookContext(_messages, cancellationToken)
            : _defaultRawContext;
        var context = _contextFactory.Create(rawContext);
        return _resultHooks.FireEntryAsync(
            entry,
            e,
            rawContext,
            context,
            options ?? ResultDispatchOptions<TResult>(),
            cancellationToken);
    }

    private ResultHookDispatchOptions<TResult> ResultDispatchOptions<TResult>()
        where TResult : struct, IHookResult
    {
        lock (_gate)
        {
            return _resultDispatchOptions.TryGetValue(typeof(TResult), out var options)
                ? (ResultHookDispatchOptions<TResult>)options
                : ResultHookDispatchOptions<TResult>.Default;
        }
    }
}
