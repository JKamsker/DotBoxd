namespace DotBoxD.Kernels.Sandbox;

using DotBoxD.Kernels;

public sealed partial class SandboxContext
{
    private DateTimeOffset? _bindingGrantClock;

    public bool AsyncEnabled => Policy.GrantsCapability(RuntimeCapabilityIds.Async, EffectiveGrantClock);

    internal IDisposable BeginBindingGrantClockScope(DateTimeOffset now)
    {
        var previous = _bindingGrantClock;
        _bindingGrantClock = now;
        return new BindingGrantClockScope(this, previous);
    }

    private DateTimeOffset EffectiveGrantClock => _bindingGrantClock ?? Policy.GrantClock;

    private sealed class BindingGrantClockScope(
        SandboxContext context,
        DateTimeOffset? previous) : IDisposable
    {
        public void Dispose()
        {
            context._bindingGrantClock = previous;
        }
    }
}
