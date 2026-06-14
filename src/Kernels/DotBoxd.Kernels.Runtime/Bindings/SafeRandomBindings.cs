namespace DotBoxd.Kernels.Runtime;

using DotBoxd.Kernels;

public static class SafeRandomBindings
{
    public static BindingDescriptor NextI32 { get; } = new(
        "random.nextI32",
        SemVersion.One,
        [SandboxType.I32, SandboxType.I32],
        SandboxType.I32,
        SandboxEffect.Cpu | SandboxEffect.Random,
        "random",
        BindingCostModel.Fixed(3),
        AuditLevel.PerCall,
        BindingSafety.ReadOnlyExternal,
        (context, args, _) => {
            var startedAt = DateTimeOffset.UtcNow;
            var min = ((I32Value)args[0]).Value;
            var max = ((I32Value)args[1]).Value;
            var value = context.NextRandomInt32(min, max);
            var timestamp = context.AuditTimestamp();
            context.Audit.Write(new SandboxAuditEvent(
                context.RunId,
                "BindingCall",
                timestamp,
                true,
                BindingId: "random.nextI32",
                CapabilityId: "random",
                Effect: SandboxEffect.Random,
                ResourceId: "random:i32",
                Fields: context.BindingAuditFields("random", startedAt)));
            return ValueTask.FromResult(SandboxValue.FromInt32(value));
        },
        CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));
}
