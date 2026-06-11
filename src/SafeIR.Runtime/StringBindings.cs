namespace SafeIR.Runtime;

using SafeIR;

public static class StringBindings
{
    public static IReadOnlyList<BindingDescriptor> All { get; } = [
        new(
            "string.length",
            SemVersion.One,
            [SandboxType.String],
            SandboxType.I32,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureIntrinsic,
            (_, args, _) => ValueTask.FromResult(SandboxValue.FromInt32(((StringValue)args[0]).Value.Length)),
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding))),
        new(
            "string.concatBudgeted",
            SemVersion.One,
            [SandboxType.String, SandboxType.String],
            SandboxType.String,
            SandboxEffect.Cpu | SandboxEffect.Alloc,
            null,
            BindingCostModel.PerByte(2, 1),
            AuditLevel.None,
            BindingSafety.PureIntrinsic,
            (ctx, args, _) => {
                var text = ((StringValue)args[0]).Value + ((StringValue)args[1]).Value;
                ctx.ChargeString(text);
                return ValueTask.FromResult(SandboxValue.FromString(text));
            },
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)))
    ];
}
