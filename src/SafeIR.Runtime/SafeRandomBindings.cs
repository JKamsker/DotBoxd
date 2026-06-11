namespace SafeIR.Runtime;

using SafeIR;

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
            var min = ((I32Value)args[0]).Value;
            var max = ((I32Value)args[1]).Value;
            var value = context.NextRandomInt32(min, max);
            context.Audit.Write(new SandboxAuditEvent(
                context.RunId,
                "BindingCall",
                DateTimeOffset.UtcNow,
                true,
                BindingId: "random.nextI32",
                CapabilityId: "random",
                Effect: SandboxEffect.Random,
                ResourceId: "random:i32"));
            return ValueTask.FromResult(SandboxValue.FromInt32(value));
        },
        CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));
}
