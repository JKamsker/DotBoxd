namespace SafeIR.Runtime;

using SafeIR;

public static partial class SafeLogBindings
{
    public static BindingDescriptor Info { get; } = Create("log.info", "info");

    public static BindingDescriptor Warn { get; } = Create("log.warn", "warn");

    private static BindingDescriptor Create(string id, string level)
        => new(
            id,
            SemVersion.One,
            [SandboxType.String],
            SandboxType.Unit,
            SandboxEffect.Cpu | SandboxEffect.Audit,
            "log.write",
            BindingCostModel.Fixed(2),
            AuditLevel.PerCall,
            BindingSafety.SideEffectingExternal,
            (context, args, _) =>
            {
                Write(context, id, level, ((StringValue)args[0]).Value);
                return ValueTask.FromResult(SandboxValue.Unit);
            },
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));

    private static void Write(SandboxContext context, string bindingId, string level, string message)
    {
        var startedAt = DateTimeOffset.UtcNow;
        context.ChargeLogEvent(message);
        context.ChargeStringAllocation(message.Length);
        var timestamp = context.UtcNow();
        context.Audit.Write(new SandboxAuditEvent(
            context.RunId,
            "SandboxLog",
            timestamp,
            true,
            BindingId: bindingId,
            CapabilityId: "log.write",
            Effect: SandboxEffect.Audit,
            ResourceId: $"log:{level}",
            Message: AuditTextSanitizer.SanitizeAndRedact(message),
            Fields: context.BindingAuditFields("log", startedAt)));
    }
}
