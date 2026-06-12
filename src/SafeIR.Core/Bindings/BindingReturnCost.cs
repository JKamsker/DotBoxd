namespace SafeIR;

internal static class BindingReturnCost
{
    public static long MeasureBytes(SandboxValue value)
        => SandboxValueShapeMeter.Measure(value).StringBytes;
}
