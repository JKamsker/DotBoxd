namespace SafeIR.Runtime;

using SafeIR;

public static class SafeTimeBindings
{
    public static BindingDescriptor NowUnixMillis { get; } = new(
        "time.nowUnixMillis",
        SemVersion.One,
        [],
        SandboxType.I64,
        SandboxEffect.Cpu | SandboxEffect.Time,
        "time.now",
        BindingCostModel.Fixed(2),
        AuditLevel.PerCall,
        BindingSafety.ReadOnlyExternal,
        (context, _, _) => {
            var value = context.UtcNow().ToUnixTimeMilliseconds();
            context.Audit.Write(new SandboxAuditEvent(
                context.RunId,
                "BindingCall",
                DateTimeOffset.UtcNow,
                true,
                BindingId: "time.nowUnixMillis",
                CapabilityId: "time.now",
                Effect: SandboxEffect.Time,
                ResourceId: "clock:utc"));
            return ValueTask.FromResult(SandboxValue.FromInt64(value));
        },
        CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));
}
