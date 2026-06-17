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

        var callsByBinding = CallsByBinding;
        var bindingCalls = callsByBinding.TryGetValue(bindingId, out var existing)
            ? AddBindingCallCount(existing, 1, bindingId)
            : 1;
        callsByBinding[bindingId] = bindingCalls;
        if (bindingCalls > maxCallsPerRun.Value)
        {
            throw BindingCallQuota(bindingId);
        }
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
