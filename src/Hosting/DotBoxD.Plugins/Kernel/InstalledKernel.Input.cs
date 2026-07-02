using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;
using DotBoxD.Plugins.Runtime.Input;

namespace DotBoxD.Plugins.Kernel;

public sealed partial class InstalledKernel
{
    private SandboxValue[]? _preparedInputValues;
    private ListValue? _preparedInputList;

    private SandboxValue BuildInput<TEvent>(
        IPluginEventAdapter<TEvent> adapter,
        TEvent e,
        string entrypoint,
        IReadOnlyList<Parameter> parameters)
    {
        lock (_lifecycleGate)
        {
            var deferredUpdates = _liveStateSync.SynchronizeForInput();
            return UsesReusableNoAuditInput(entrypoint)
                ? PluginKernelInputBuilder.BuildWithReusableBuffer(
                    adapter,
                    e,
                    parameters,
                    deferredUpdates,
                    Manifest.LiveSettings,
                    Value,
                    _pendingLiveUpdates.Enqueue,
                    ref _preparedInputValues,
                    ref _preparedInputList)
                : PluginKernelInputBuilder.Build(
                    adapter,
                    e,
                    parameters,
                    deferredUpdates,
                    Manifest.LiveSettings,
                    Value,
                    _pendingLiveUpdates.Enqueue);
        }
    }

    private bool UsesReusableNoAuditInput(string entrypoint)
        => _executionMode == ExecutionMode.Compiled &&
           string.Equals(entrypoint, _entrypoints.ShouldHandle, StringComparison.Ordinal) &&
           _plan.BindingReferences.TryGetValue(entrypoint, out var bindings) &&
           bindings.Count == 0;

    // Defensively copies a reused-buffer input before handing it to a second execution (the Handle
    // hop after a reusing ShouldHandle). A shallow wrapper copy would still share the kernel's
    // backing array, so a later same-shaped dispatch overwriting that array in place could corrupt a
    // reference the second execution retained past the call (e.g. through escaped async). Copying the
    // current elements into a fresh owned array makes the snapshot independent of any later reuse.
    // Internal for regression coverage (see Fix_PAL_0046).
    internal static SandboxValue SnapshotInput(SandboxValue input)
        => input is ListValue list
            ? SandboxValue.FromOwnedList([.. list], list.ItemType)
            : input;
}
