using System.Collections.Concurrent;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Plugins.Runtime.Hooks;

/// <summary>
/// Delivers an encoded, filtered+projected value to the plugin's native <c>RunLocal</c> delegate across the
/// control-plane callback. The server-side pipeline invokes this once per event that passes the lowered filter;
/// the implementation forwards <paramref name="projectedValue"/> over the IPC boundary keyed by
/// <paramref name="subscriptionId"/>. Transport-agnostic so the pipeline does not depend on the RPC layer.
/// </summary>
public delegate ValueTask RemoteLocalPush(string subscriptionId, byte[] projectedValue, CancellationToken cancellationToken);

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

        var expectedType = KernelRpcMarshaller.SandboxTypeOf(typeof(TProjected));
        var entry = new Handler(expectedType, async (sandboxValue, context) =>
        {
            var projected = (TProjected)KernelRpcMarshaller.FromSandboxValue(sandboxValue, typeof(TProjected))!;
            await handler(projected, context).ConfigureAwait(false);
        });

        _handlers[subscriptionId] = entry;
        return new Registration(this, subscriptionId);
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

        var wireValue = KernelRpcBinaryCodec.DecodeValue(projectedValue);
        var sandboxValue = KernelRpcValueConverter.ToSandboxValue(wireValue, handler.ExpectedType);
        await handler.Invoke(sandboxValue, context).ConfigureAwait(false);
    }

    /// <summary>Removes the handler for <paramref name="subscriptionId"/>. Returns <c>true</c> if one was present.</summary>
    public bool Unregister(string subscriptionId)
        => !string.IsNullOrEmpty(subscriptionId) && _handlers.TryRemove(subscriptionId, out _);

    /// <summary>
    /// Removes all registered handlers. Called when the plugin connection tears down (session disposed / peer
    /// disconnected) so a dropped plugin's callbacks do not linger.
    /// </summary>
    public void Clear() => _handlers.Clear();

    private sealed record Handler(SandboxType ExpectedType, Func<SandboxValue, HookContext, ValueTask> Invoke);

    private sealed class Registration(RemoteLocalHandlerRegistry owner, string subscriptionId) : IDisposable
    {
        private RemoteLocalHandlerRegistry? _owner = owner;

        public void Dispose()
            => Interlocked.Exchange(ref _owner, null)?.Unregister(subscriptionId);
    }
}
