using System.Collections.Concurrent;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Plugins.Runtime.Hooks;

/// <summary>
/// Delivers an encoded, filtered+projected value to the plugin's native <c>RunLocal</c> delegate across the
/// control-plane callback. The server-side pipeline invokes this once per event that passes the lowered filter;
/// the implementation forwards <paramref name="projectedValue"/> over the IPC boundary keyed by
/// <paramref name="subscriptionId"/>. Transport-agnostic so the pipeline does not depend on the RPC layer.
/// </summary>
public delegate ValueTask RemoteLocalPush(string subscriptionId, ReadOnlyMemory<byte> projectedValue, CancellationToken cancellationToken);

public delegate ValueTask<byte[]> RemoteLocalResultRequest(
    string subscriptionId,
    ReadOnlyMemory<byte> contextValue,
    CancellationToken cancellationToken);

/// <summary>
/// Client-side registry for remote <c>RunLocal</c> terminals. A remote
/// <c>server.Hooks.On&lt;TEvent&gt;().Where(..).Select(..).RunLocal(λ)</c> chain lowers only its
/// <c>Where</c>/<c>Select</c> stages to verified IR that filters and projects server-side; the projected
/// value is pushed back over the control-plane callback per passing event. This registry holds the native
/// <c>RunLocal</c> delegate (real host C#, never lowered), keyed by the subscription id returned at install
/// time, and decodes each pushed payload back to the projected CLR type before invoking that delegate.
/// </summary>
/// <remarks>
/// The decode mirrors the server-extension request/response path in reverse: the same
/// <see cref="KernelRpcBinaryCodec"/>/<see cref="KernelRpcValueConverter"/>/<see cref="KernelRpcMarshaller"/>
/// converters carry the value, so the supported projection types are exactly the wire-eligible set
/// (bool, int, long, double, string, enums, lists/arrays, and DTO records).
/// </remarks>
public sealed class RemoteLocalHandlerRegistry
{
    private readonly ConcurrentDictionary<string, Handler> _handlers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ResultHandler> _resultHandlers = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers the native terminal delegate for a lowered local chain. <typeparamref name="TProjected"/> is
    /// the type produced by the chain's final <c>Select</c> (or the event type when there is no projection — a
    /// whole-event chain). Idempotent: re-registering the same <paramref name="subscriptionId"/> replaces the
    /// previous handler, so a plugin that reconnects and re-installs with a reused id does not throw. Returns a
    /// token that unregisters this handler when disposed.
    /// </summary>
    public IDisposable Register<TProjected>(
        string subscriptionId,
        Func<TProjected, HookContext, ValueTask> handler)
    {
        ArgumentException.ThrowIfNullOrEmpty(subscriptionId);
        ArgumentNullException.ThrowIfNull(handler);

        // Runtime fallback: validate the projected type once, then decode straight from KernelRpcValue. This is
        // used when the projected type has no generated decoder, so it avoids the SandboxValue graph on dispatch.
        _ = KernelRpcMarshaller.SandboxTypeOf(typeof(TProjected));
        return RegisterKernelHandler(subscriptionId, (wireValue, context) =>
        {
            var projected = (TProjected)KernelRpcMarshaller.FromKernelRpcValue(wireValue, typeof(TProjected))!;
            return handler(projected, context);
        });
    }

    /// <summary>
    /// Registers the native terminal delegate alongside a generated reflection-free <paramref name="decoder"/>
    /// that reads <typeparamref name="TProjected"/> straight off the pushed <see cref="KernelRpcValue"/>'s typed
    /// fields — no <c>SandboxValue</c> intermediate, no boxing, no reflection. Emitted by the plugin generator
    /// for wire-eligible projected types; <see cref="Register{TProjected}(string, Func{TProjected, HookContext, ValueTask})"/>
    /// remains the fallback for the rest. Idempotent in the same way as the 2-arg overload.
    /// </summary>
    public IDisposable Register<TProjected>(
        string subscriptionId,
        Func<TProjected, HookContext, ValueTask> handler,
        Func<KernelRpcValue, TProjected> decoder)
    {
        ArgumentException.ThrowIfNullOrEmpty(subscriptionId);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(decoder);

        return RegisterKernelHandler(subscriptionId, (wireValue, context) =>
        {
            var projected = decoder(wireValue);
            return handler(projected, context);
        });
    }

    /// <summary>
    /// Registers a generated decoder that reads directly from the pushed binary payload. This is the fastest
    /// generated path because dispatch does not materialize an intermediate <see cref="KernelRpcValue"/> tree.
    /// </summary>
    public IDisposable Register<TProjected>(
        string subscriptionId,
        Func<TProjected, HookContext, ValueTask> handler,
        Func<ReadOnlyMemory<byte>, TProjected> decoder)
    {
        ArgumentException.ThrowIfNullOrEmpty(subscriptionId);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(decoder);

        return RegisterRawHandler(subscriptionId, (payload, context) =>
        {
            var projected = decoder(payload);
            return handler(projected, context);
        });
    }

    public IDisposable RegisterResult<TContext, TResult>(
        string subscriptionId,
        Func<TContext, HookContext, TResult> handler)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RegisterResult<TContext, TResult>(
            subscriptionId,
            (context, hookContext, _) => new ValueTask<TResult>(handler(context, hookContext)));
    }

    public IDisposable RegisterResult<TContext, TResult>(
        string subscriptionId,
        Func<TContext, HookContext, CancellationToken, ValueTask<TResult>> handler)
        where TResult : struct, IHookResult
    {
        ArgumentException.ThrowIfNullOrEmpty(subscriptionId);
        ArgumentNullException.ThrowIfNull(handler);
        _ = KernelRpcMarshaller.SandboxTypeOf(typeof(TContext));
        _ = KernelRpcMarshaller.SandboxTypeOf(typeof(TResult));

        var entry = new ResultHandler(async (payload, hookContext, cancellationToken) =>
        {
            var wireValue = KernelRpcBinaryCodec.DecodeValue(payload);
            var context = (TContext)KernelRpcMarshaller.FromKernelRpcValue(wireValue, typeof(TContext))!;
            var result = await handler(context, hookContext, cancellationToken).ConfigureAwait(false);
            return RemoteLocalResultEncoder.Encode(result);
        });
        _resultHandlers[subscriptionId] = entry;
        return new ResultRegistration(this, subscriptionId, entry);
    }

    private IDisposable RegisterKernelHandler(string subscriptionId, Func<KernelRpcValue, HookContext, ValueTask> invoke)
        => RegisterRawHandler(subscriptionId, (payload, context) =>
        {
            var wireValue = KernelRpcBinaryCodec.DecodeValue(payload);
            return invoke(wireValue, context);
        });

    private IDisposable RegisterRawHandler(string subscriptionId, Func<ReadOnlyMemory<byte>, HookContext, ValueTask> invoke)
    {
        var entry = new Handler(invoke);
        _handlers[subscriptionId] = entry;
        return new Registration(this, subscriptionId, entry);
    }

    /// <summary>
    /// Decodes a server-pushed projected payload back to the projected CLR type and invokes the registered
    /// native delegate. <paramref name="context"/> is the client-side <see cref="HookContext"/> the delegate
    /// runs against. Throws if no handler is registered for <paramref name="subscriptionId"/>.
    /// </summary>
    public async ValueTask DispatchAsync(
        string subscriptionId,
        ReadOnlyMemory<byte> projectedValue,
        HookContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(subscriptionId);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_handlers.TryGetValue(subscriptionId, out var handler))
        {
            throw new InvalidOperationException(
                $"No remote local handler is registered for subscription '{subscriptionId}'.");
        }

        await handler.Invoke(projectedValue, context).ConfigureAwait(false);
    }

    public async ValueTask<byte[]> DispatchResultAsync(
        string subscriptionId,
        ReadOnlyMemory<byte> contextValue,
        HookContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(subscriptionId);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_resultHandlers.TryGetValue(subscriptionId, out var handler))
        {
            throw new InvalidOperationException(
                $"No remote local result handler is registered for subscription '{subscriptionId}'.");
        }

        return await handler.Invoke(contextValue, context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes the handler for <paramref name="subscriptionId"/>. Returns <c>true</c> if one was present.</summary>
    public bool Unregister(string subscriptionId)
    {
        if (string.IsNullOrEmpty(subscriptionId))
        {
            return false;
        }

        var removedHandler = _handlers.TryRemove(subscriptionId, out _);
        var removedResult = _resultHandlers.TryRemove(subscriptionId, out _);
        return removedHandler || removedResult;
    }

    private bool Unregister(string subscriptionId, Handler handler)
        => !string.IsNullOrEmpty(subscriptionId) &&
           ((ICollection<KeyValuePair<string, Handler>>)_handlers).Remove(
               new KeyValuePair<string, Handler>(subscriptionId, handler));

    private bool Unregister(string subscriptionId, ResultHandler handler)
        => !string.IsNullOrEmpty(subscriptionId) &&
           ((ICollection<KeyValuePair<string, ResultHandler>>)_resultHandlers).Remove(
               new KeyValuePair<string, ResultHandler>(subscriptionId, handler));

    /// <summary>
    /// Removes all registered handlers. Called when the plugin connection tears down (session disposed / peer
    /// disconnected) so a dropped plugin's callbacks do not linger.
    /// </summary>
    public void Clear()
    {
        _handlers.Clear();
        _resultHandlers.Clear();
    }

    private sealed class Handler(Func<ReadOnlyMemory<byte>, HookContext, ValueTask> invoke)
    {
        public Func<ReadOnlyMemory<byte>, HookContext, ValueTask> Invoke { get; } = invoke;
    }

    private sealed class ResultHandler(
        Func<ReadOnlyMemory<byte>, HookContext, CancellationToken, ValueTask<byte[]>> invoke)
    {
        public Func<ReadOnlyMemory<byte>, HookContext, CancellationToken, ValueTask<byte[]>> Invoke { get; } = invoke;
    }

    private sealed class Registration(
        RemoteLocalHandlerRegistry owner,
        string subscriptionId,
        Handler handler) : IDisposable
    {
        private RemoteLocalHandlerRegistry? _owner = owner;

        public void Dispose()
            => Interlocked.Exchange(ref _owner, null)?.Unregister(subscriptionId, handler);
    }

    private sealed class ResultRegistration(
        RemoteLocalHandlerRegistry owner,
        string subscriptionId,
        ResultHandler handler) : IDisposable
    {
        private RemoteLocalHandlerRegistry? _owner = owner;

        public void Dispose()
            => Interlocked.Exchange(ref _owner, null)?.Unregister(subscriptionId, handler);
    }
}
