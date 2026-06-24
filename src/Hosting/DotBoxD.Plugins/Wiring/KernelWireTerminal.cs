using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Plugins;

/// <summary>
/// How an installed kernel's verified terminal must be wired into a hook or subscription pipeline. This is the
/// single classification the host-side router (<see cref="PluginServer.WireHook"/> /
/// <see cref="PluginServer.WireSubscription"/>) computes once from install-owned + verified metadata, replacing
/// the per-host boolean lattice (is-local-terminal / is-result / is-result-local-terminal) every host used to
/// hand-roll.
/// </summary>
public enum KernelWireKind
{
    /// <summary>Run the verified kernel entirely server-side (<c>Use</c>).</summary>
    Plain,

    /// <summary>Project server-side and push the projected value to the plugin's native delegate — a remote <c>RunLocal</c> (<c>UseProjecting</c>).</summary>
    Projecting,

    /// <summary>The sandbox <c>Handle</c> entrypoint returns the result directly — a sandbox <c>Register</c> (<c>UseResult</c>).</summary>
    Result,

    /// <summary>Filter server-side and request the result from the plugin process — a remote <c>RegisterLocal</c> (<c>UseProjectingResult</c>).</summary>
    ProjectingResult,
}

/// <summary>
/// The trusted terminal classification for an <see cref="InstalledKernel"/>: which pipeline terminal to wire
/// and the arguments it needs. Recomputed from verified IR / install-owned metadata — never trusted from the
/// raw manifest — so a single audited <see cref="Classify"/> replaces the hand-written routing every host used
/// to copy.
/// </summary>
/// <param name="Kind">Which terminal to wire.</param>
/// <param name="CallbackSubscriptionId">The install-owned callback route id for a projecting terminal; <c>null</c> otherwise.</param>
/// <param name="ResultType">The result type for a result terminal; <c>null</c> otherwise.</param>
/// <param name="Priority">The host dispatch priority for a result terminal; <c>0</c> otherwise.</param>
public readonly record struct KernelWireTerminal(
    KernelWireKind Kind,
    string? CallbackSubscriptionId,
    Type? ResultType,
    int Priority)
{
    /// <summary>
    /// Classifies how <paramref name="kernel"/> must be wired, from its install-owned callback id and the
    /// verified subscription metadata. <paramref name="hookResultType"/> is the result type declared by the
    /// subscribed event's <c>[Hook]</c> attribute (supplied by the resolved adapter so the router needs no
    /// reflection at wire time); it is required when the subscription declares a result and is otherwise unused.
    /// </summary>
    internal static KernelWireTerminal Classify(InstalledKernel kernel, Type? hookResultType)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        var subscription = kernel.Manifest.Subscriptions.Count > 0
            ? kernel.Manifest.Subscriptions[0]
            : null;
        var callbackId = kernel.CallbackSubscriptionId;

        if (subscription?.ResultType is not null)
        {
            var resultType = hookResultType ?? throw new InvalidOperationException(
                $"Plugin '{kernel.Manifest.PluginId}' installed a result hook, but the subscribed event type has no [Hook] result declaration.");
            var kind = callbackId is not null && subscription.ResultLocalTerminal
                ? KernelWireKind.ProjectingResult
                : KernelWireKind.Result;
            return new KernelWireTerminal(kind, callbackId, resultType, subscription.Priority);
        }

        if (callbackId is not null && subscription?.LocalTerminal == true)
        {
            return new KernelWireTerminal(KernelWireKind.Projecting, callbackId, ResultType: null, subscription.Priority);
        }

        return new KernelWireTerminal(KernelWireKind.Plain, CallbackSubscriptionId: null, ResultType: null, subscription?.Priority ?? 0);
    }
}
