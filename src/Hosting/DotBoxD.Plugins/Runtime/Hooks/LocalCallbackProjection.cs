using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime.Input;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Plugins.Runtime.Hooks;

/// <summary>
/// Shared per-event push logic for lowered remote <c>RunLocal</c> chains, used by both the hook and the
/// subscription pipeline. Current generated packages always carry an explicit projected type, including
/// no-Select <c>RunLocal</c> chains where the projected value is the event record itself. The legacy
/// <c>ProjectedType == null</c> branch is retained only as a defensive runtime fallback; install validation
/// rejects new packages that try to use it.
/// <list type="bullet">
///   <item><b>Projection</b> (<c>ProjectedType != null</c>): the lowered <c>Handle</c> returns the
///   <c>Select</c> value; the host pushes that value.</item>
///   <item><b>Legacy whole-event</b> (<c>ProjectedType == null</c>): the lowered <c>Handle</c> returns
///   <c>Unit</c>; the host evaluates only the <c>Where</c> filter and pushes the whole event record.</item>
/// </list>
/// In both kinds the filter runs server-side in the sandbox <i>before</i> anything crosses the wire, so a
/// non-matching event produces no push — the premise that filtering is server-side and IPC carries only the
/// already-accepted payload.
/// </summary>
internal static class LocalCallbackProjection
{
    /// <summary>True when the local-terminal subscription pushes the whole event (no <c>Select</c>).</summary>
    public static bool IsWholeEvent(PluginManifest manifest)
    {
        bool? wholeEvent = null;
        foreach (var subscription in manifest.Subscriptions)
        {
            if (!subscription.LocalTerminal)
            {
                continue;
            }

            var current = subscription.ProjectedType is null;
            if (wholeEvent is { } expected && expected != current)
            {
                throw new InvalidOperationException(
                    $"Plugin '{manifest.PluginId}' has conflicting local-terminal projected type declarations.");
            }

            wholeEvent = current;
        }

        return wholeEvent ?? false;
    }

    /// <summary>
    /// Fail-fast install-time check: a whole-event chain needs a value-writer adapter so the host can build
    /// the event record to push. The framework's convention adapter is always a value-writer, so this only
    /// trips on a hand-written adapter that does not write event values.
    /// </summary>
    public static void EnsureWholeEventSupported<TEvent>(IPluginEventAdapter<TEvent> adapter)
    {
        if (adapter is not IPluginEventValueWriter<TEvent>)
        {
            throw new InvalidOperationException(
                $"A whole-event RunLocal chain for '{typeof(TEvent).Name}' requires an event value-writer adapter " +
                "(the framework generates one for event records).");
        }

        if (ContainsPolymorphicHandle(typeof(TEvent)))
        {
            throw new InvalidOperationException(
                $"A whole-event local callback for '{typeof(TEvent).Name}' cannot carry polymorphic handle " +
                "properties because the wire payload contains sandbox keys, not host handle instances. " +
                "Project scalar values before RunLocal/RegisterLocal.");
        }
    }

    /// <summary>
    /// Runs the lowered filter (and projection, for a projection chain) server-side in the sandbox; on a
    /// match, encodes the payload — the projected value, or the whole event record — and hands it to
    /// <paramref name="push"/>. A non-matching event returns without pushing.
    /// </summary>
    public static async ValueTask PushAsync<TEvent>(
        InstalledKernel kernel,
        IPluginEventAdapter<TEvent> adapter,
        TEvent e,
        HookContext context,
        bool wholeEvent,
        string subscriptionId,
        RemoteLocalPush push)
    {
        SandboxValue payloadValue;
        if (wholeEvent)
        {
            // Whole-event: evaluate only the lowered Where filter (server-side, sandboxed); on a match push
            // the entire event record.
            if (!await kernel.ShouldHandleAsync(adapter, e, context.CancellationToken).ConfigureAwait(false))
            {
                return;
            }

            payloadValue = BuildEventRecord(adapter, e);
        }
        else
        {
            // Projection: run the lowered Where + Select; the Handle returns the projected value to push.
            var projection = await kernel.InvokeProjectingAsync(adapter, e, context.CancellationToken).ConfigureAwait(false);
            if (!projection.Matched)
            {
                return;
            }

            payloadValue = projection.Value;
        }

        // Encode into a pooled buffer and hand its written span straight to the transport — no per-event
        // MemoryStream, growth, or ToArray copy. The writer is disposed (its rented array returned to the pool)
        // only when this method's scope exits, i.e. AFTER await push completes and the transport has finished
        // copying the bytes; returning it sooner would alias a buffer the send still reads from.
        using var writer = PooledRpcBufferWriter.Rent();
        KernelRpcBinaryCodec.EncodeValue(payloadValue, writer);
        await push(subscriptionId, writer.WrittenMemory, context.CancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask<byte[]> RequestResultAsync<TEvent>(
        IPluginEventAdapter<TEvent> adapter,
        TEvent e,
        HookContext context,
        string subscriptionId,
        RemoteLocalResultRequest request,
        CancellationToken cancellationToken)
    {
        EnsureWholeEventSupported(adapter);
        using var writer = PooledRpcBufferWriter.Rent();
        KernelRpcBinaryCodec.EncodeValue(BuildEventRecord(adapter, e), writer);
        return await request(subscriptionId, writer.WrittenMemory, cancellationToken).ConfigureAwait(false);
    }

    // The event record's field order is the adapter's value-writer order, which the convention adapter derives
    // from the event record's constructor/declaration order — the same order KernelRpcMarshaller uses to
    // reconstruct the DTO on the client, so the round-trip preserves field identity.
    private static SandboxValue BuildEventRecord<TEvent>(IPluginEventAdapter<TEvent> adapter, TEvent e)
    {
        var writer = (IPluginEventValueWriter<TEvent>)adapter;
        var values = new SandboxValue[writer.EventValueCount];
        writer.CopySandboxValues(e, values, 0);
        PluginEventValueWriterValueValidator.ValidateCopiedValues(writer, values, 0);
        return SandboxValue.FromOwnedRecord(values);
    }

    private static bool ContainsPolymorphicHandle(Type eventType)
    {
        foreach (var property in eventType.GetProperties())
        {
            if (property.GetIndexParameters().Length == 0 &&
                PolymorphicHandleRuntimeMetadataReader.TryResolve(property.PropertyType, out _))
            {
                return true;
            }
        }

        return false;
    }
}
