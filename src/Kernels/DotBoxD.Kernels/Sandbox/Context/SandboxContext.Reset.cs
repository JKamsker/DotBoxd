namespace DotBoxD.Kernels.Sandbox;

public sealed partial class SandboxContext
{
    internal void ResetForCompiledNoAuditReuse()
    {
        _deterministicRandom = null;
        _returnCredits = null;
        _bindingGrantClock = null;
        _lastCapabilityId = null;
        _lastCapabilityClock = default;
        _lastCapabilityGrant = null;
        _lastBindingId = null;
        _lastBindingDescriptor = null;
        _callDepth = 0;
    }
}
