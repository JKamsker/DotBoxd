namespace DotBoxD.Kernels.Model;

public sealed partial class ResourceMeter
{
    public void ChargeHostCall(string bindingId, int? maxCallsPerRun = null)
    {
        HostCalls = AddHostCallCount(HostCalls, 1, bindingId);
        if (HostCalls > Limits.MaxHostCalls)
        {
            throw HostCallQuota(bindingId);
        }

        if (maxCallsPerRun is null)
        {
            return;
        }

        ChargeLimitedBindingCall(bindingId, maxCallsPerRun.Value);
    }

    private void ChargeLimitedBindingCall(string bindingId, int maxCallsPerRun)
    {
        if (string.Equals(_lastLimitedBindingId, bindingId, StringComparison.Ordinal))
        {
            var cachedCalls = AddBindingCallCount(_lastLimitedBindingCalls, 1, bindingId);
            _lastLimitedBindingCalls = cachedCalls;
            if (cachedCalls > maxCallsPerRun)
            {
                throw BindingCallQuota(bindingId);
            }

            return;
        }

        FlushLastLimitedBinding();
        var callsByBinding = _callsByBinding;
        var bindingCalls = callsByBinding is not null && callsByBinding.TryGetValue(bindingId, out var existing)
            ? AddBindingCallCount(existing, 1, bindingId)
            : 1;
        _lastLimitedBindingId = bindingId;
        _lastLimitedBindingCalls = bindingCalls;
        if (bindingCalls > maxCallsPerRun)
        {
            throw BindingCallQuota(bindingId);
        }
    }

    private void FlushLastLimitedBinding()
    {
        if (_lastLimitedBindingId is null)
        {
            return;
        }

        CallsByBinding[_lastLimitedBindingId] = _lastLimitedBindingCalls;
        _lastLimitedBindingId = null;
        _lastLimitedBindingCalls = 0;
    }

    private Dictionary<string, int> CallsByBinding
        => _callsByBinding ??= new Dictionary<string, int>(StringComparer.Ordinal);

    private static SandboxRuntimeException HostCallQuota(string bindingId)
        => Quota($"host call budget exhausted at {bindingId}");

    private static SandboxRuntimeException BindingCallQuota(string bindingId)
        => Quota($"binding call budget exhausted at {bindingId}");

    private static int AddHostCallCount(int current, int amount, string bindingId)
    {
        try
        {
            return checked(current + amount);
        }
        catch (OverflowException)
        {
            throw HostCallQuota(bindingId);
        }
    }

    private static int AddBindingCallCount(int current, int amount, string bindingId)
    {
        try
        {
            return checked(current + amount);
        }
        catch (OverflowException)
        {
            throw BindingCallQuota(bindingId);
        }
    }
}
