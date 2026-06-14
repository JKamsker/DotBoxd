namespace DotBoxd.Kernels;

public sealed partial class SandboxContext
{
    internal bool CanBulkChargeFuel(long fuelPerUnit, long units)
    {
        if (fuelPerUnit < 0 || units < 0)
        {
            return false;
        }

        try
        {
            return Budget.CanChargeFuel(CheckedFuel(units, fuelPerUnit));
        }
        catch (SandboxRuntimeException)
        {
            return false;
        }
    }

    internal void ChargeBulkFuel(long fuelPerUnit, long units)
    {
        if (units == 0)
        {
            return;
        }

        if (!CanBulkChargeFuel(fuelPerUnit, units))
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.QuotaExceeded,
                "fuel exhausted"));
        }

        ChargeFuel(CheckedFuel(units, fuelPerUnit));
    }

    internal bool CanBulkChargeValue(SandboxValue value, long count)
        => value is StringValue text && Budget.CanChargeStringValues(text.Value, count);

    internal void ChargeBulkValue(SandboxValue value, long count)
    {
        if (value is not StringValue text)
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.ValidationError,
                "bulk value charging supports string literals only"));
        }

        CancellationToken.ThrowIfCancellationRequested();
        Budget.ChargeStringValues(text.Value, count);
    }

    internal bool CanBulkChargeBindingCalls(BindingDescriptor descriptor, long calls)
    {
        if (calls < 0 ||
            descriptor.RequiredCapability is not null ||
            descriptor.CostModel.MaxCallsPerRun is not null ||
            AllowedBindingIds is not null && !AllowedBindingIds.Contains(descriptor.Id) ||
            !Budget.CanChargeHostCalls(calls))
        {
            return false;
        }

        try
        {
            return Budget.CanChargeFuel(CheckedFuel(calls, descriptor.CostModel.BaseFuel));
        }
        catch (SandboxRuntimeException)
        {
            return false;
        }
    }

    internal void ChargeBindingCalls(BindingDescriptor descriptor, long calls)
    {
        if (calls == 0)
        {
            return;
        }

        if (!CanBulkChargeBindingCalls(descriptor, calls))
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.QuotaExceeded,
                $"binding call budget exhausted at {descriptor.Id}"));
        }

        Budget.ChargeHostCalls(descriptor.Id, calls);
        ChargeFuel(CheckedFuel(calls, descriptor.CostModel.BaseFuel));
    }
}
