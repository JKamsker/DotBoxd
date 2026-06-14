namespace DotBoxd.Kernels.Runtime;

using DotBoxd.Kernels;

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
            var startedAt = DateTimeOffset.UtcNow;
            var timestamp = context.UtcNow();
            var value = timestamp.ToUnixTimeMilliseconds();
            context.Audit.Write(new SandboxAuditEvent(
                context.RunId,
                "BindingCall",
                timestamp,
                true,
                BindingId: "time.nowUnixMillis",
                CapabilityId: "time.now",
                Effect: SandboxEffect.Time,
                ResourceId: "clock:utc",
                Fields: context.BindingAuditFields("clock", startedAt)));
            return ValueTask.FromResult(SandboxValue.FromInt64(value));
        },
        CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));
}
