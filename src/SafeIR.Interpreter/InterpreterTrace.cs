namespace SafeIR.Interpreter;

using SafeIR;

internal static class InterpreterTrace
{
    public static void Write(
        SandboxContext context,
        SandboxExecutionOptions options,
        string moduleHash,
        string functionId,
        string category,
        string nodeKind)
    {
        if (!options.EnableDebugTrace)
        {
            return;
        }

        context.Audit.Write(new SandboxAuditEvent(
            context.RunId,
            "DebugTrace",
            DateTimeOffset.UtcNow,
            true,
            Message: $"moduleHash={moduleHash} function={functionId} node={category}:{nodeKind} fuelRemaining={RemainingFuel(context)}"));
    }

    public static void WriteBindingCall(
        SandboxContext context,
        SandboxExecutionOptions options,
        string moduleHash,
        string functionId,
        BindingDescriptor binding)
    {
        if (!options.EnableDebugTrace)
        {
            return;
        }

        context.Audit.Write(new SandboxAuditEvent(
            context.RunId,
            "DebugTrace",
            DateTimeOffset.UtcNow,
            true,
            BindingId: binding.Id,
            CapabilityId: binding.RequiredCapability,
            Effect: binding.Effects,
            Message: $"moduleHash={moduleHash} function={functionId} hostCall={binding.Id} " +
                     $"capability={binding.RequiredCapability ?? "none"} effects={binding.Effects} " +
                     $"fuelRemaining={RemainingFuel(context)}"));
    }

    private static long RemainingFuel(SandboxContext context)
        => context.Budget.Limits.MaxFuel - context.Budget.FuelUsed;
}
