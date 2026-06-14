namespace DotBoxd.Kernels;

public sealed partial class ResourceMeter
{
    public void ChargeLoopIterations(long iterations, long fuelPerIteration)
    {
        if (iterations < 0) { throw new ArgumentOutOfRangeException(nameof(iterations)); }
        if (fuelPerIteration <= 0) { throw new ArgumentOutOfRangeException(nameof(fuelPerIteration)); }
        if (iterations == 0)
        {
            return;
        }

        LoopIterations = AddChecked(LoopIterations, iterations, "loop iteration budget exhausted");
        if (LoopIterations > Limits.MaxLoopIterations)
        {
            throw Quota("loop iteration budget exhausted");
        }

        ChargeFuel(MultiplyChecked(iterations, fuelPerIteration, "fuel exhausted"));
    }

    internal bool CanChargeLoopIterations(long iterations, long fuelPerIteration)
    {
        if (iterations < 0 || fuelPerIteration <= 0)
        {
            return false;
        }

        try
        {
            return LoopIterations <= Limits.MaxLoopIterations - iterations &&
                   CanChargeFuel(MultiplyChecked(iterations, fuelPerIteration, "fuel exhausted"));
        }
        catch (SandboxRuntimeException)
        {
            return false;
        }
    }

    internal bool CanChargeFuel(long amount)
        => amount >= 0 && FuelUsed <= Limits.MaxFuel - amount;

    internal bool CanChargeHostCalls(long calls)
        => calls >= 0 &&
           calls <= int.MaxValue &&
           HostCalls <= Limits.MaxHostCalls - calls;

    internal void ChargeHostCalls(string bindingId, long calls)
    {
        if (!CanChargeHostCalls(calls))
        {
            throw Quota($"host call budget exhausted at {bindingId}");
        }

        var count = checked((int)calls);
        HostCalls = AddChecked(HostCalls, count, $"host call budget exhausted at {bindingId}");
        var bindingCalls = _callsByBinding.TryGetValue(bindingId, out var existing)
            ? AddChecked(existing, count, $"binding call budget exhausted at {bindingId}")
            : count;
        _callsByBinding[bindingId] = bindingCalls;
    }

    internal bool CanChargeStringValues(string value, long count)
    {
        if (count < 0 || value.Length > Limits.MaxStringLength)
        {
            return false;
        }

        try
        {
            var bytes = MultiplyChecked(
                SandboxLiteralConstraints.StringByteCount(value.Length),
                count,
                "string byte budget exhausted");
            return AllocatedBytes <= Limits.MaxAllocatedBytes - bytes &&
                   StringBytes <= Limits.MaxTotalStringBytes - bytes;
        }
        catch (SandboxRuntimeException)
        {
            return false;
        }
    }

    internal void ChargeStringValues(string value, long count)
    {
        if (count == 0)
        {
            return;
        }

        if (!CanChargeStringValues(value, count))
        {
            throw Quota(value.Length > Limits.MaxStringLength
                ? "string length budget exhausted"
                : "string byte budget exhausted");
        }

        var bytes = MultiplyChecked(
            SandboxLiteralConstraints.StringByteCount(value.Length),
            count,
            "string byte budget exhausted");
        ChargeAllocation(bytes);
        StringBytes = AddChecked(StringBytes, bytes, "string byte budget exhausted");
    }
}
