namespace SafeIR.Interpreter.Internal;

using SafeIR;

internal static class InterpreterTrace
{
    public static void Write(
        SandboxContext context,
        SandboxExecutionOptions options,
        string moduleHash,
        string functionId,
        string category,
        string nodeKind,
        SourceSpan span)
    {
        if (!options.EnableDebugTrace)
        {
            return;
        }

        context.Audit.Write(new SandboxAuditEvent(
            context.RunId,
            "DebugTrace",
            context.AuditTimestamp(),
            true,
            Message: $"moduleHash={moduleHash} function={functionId} node={category}:{nodeKind} fuelRemaining={RemainingFuel(context)}",
            Fields: DebugFields(moduleHash, functionId, category, nodeKind, span, context)));
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
            context.AuditTimestamp(),
            true,
            BindingId: binding.Id,
            CapabilityId: binding.RequiredCapability,
            Effect: binding.Effects,
            Message: $"moduleHash={moduleHash} function={functionId} hostCall={binding.Id} " +
                     $"capability={binding.RequiredCapability ?? "none"} effects={binding.Effects} " +
                     $"fuelRemaining={RemainingFuel(context)}",
            Fields: DebugFields(moduleHash, functionId, "binding", binding.Id, new SourceSpan(0, 0), context)));
    }

    private static IReadOnlyDictionary<string, string> DebugFields(
        string moduleHash,
        string functionId,
        string category,
        string nodeKind,
        SourceSpan span,
        SandboxContext context)
        => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["moduleHash"] = moduleHash,
            ["functionId"] = functionId,
            ["category"] = category,
            ["nodeKind"] = nodeKind,
            ["sourceLine"] = span.Line.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["sourceColumn"] = span.Column.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["fuelRemaining"] = RemainingFuel(context).ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

    private static long RemainingFuel(SandboxContext context)
        => context.Budget.Limits.MaxFuel - context.Budget.FuelUsed;
}
