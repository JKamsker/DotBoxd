namespace SafeIR.Runtime;

using SafeIR;

public static class MathBindings
{
    public static IReadOnlyList<BindingDescriptor> All { get; } = [
        Pure("math.abs", [SandboxType.I32], SandboxType.I32, args => CompiledRuntime.AbsI32(args[0]), nameof(CompiledRuntime.AbsI32)),
        Pure("math.min", [SandboxType.I32, SandboxType.I32], SandboxType.I32, args => CompiledRuntime.MinI32(args[0], args[1]), nameof(CompiledRuntime.MinI32)),
        Pure("math.max", [SandboxType.I32, SandboxType.I32], SandboxType.I32, args => CompiledRuntime.MaxI32(args[0], args[1]), nameof(CompiledRuntime.MaxI32)),
        Pure("math.clamp", [SandboxType.I32, SandboxType.I32, SandboxType.I32], SandboxType.I32, args => CompiledRuntime.ClampI32(args[0], args[1], args[2]), nameof(CompiledRuntime.ClampI32)),
        Pure("math.sqrt", [SandboxType.F64], SandboxType.F64, args => CompiledRuntime.SqrtF64(args[0]), nameof(CompiledRuntime.SqrtF64)),
        Pure("math.floor", [SandboxType.F64], SandboxType.F64, args => CompiledRuntime.FloorF64(args[0]), nameof(CompiledRuntime.FloorF64)),
        Pure("math.ceil", [SandboxType.F64], SandboxType.F64, args => CompiledRuntime.CeilF64(args[0]), nameof(CompiledRuntime.CeilF64)),
        Pure("math.round", [SandboxType.F64], SandboxType.F64, args => CompiledRuntime.RoundF64(args[0]), nameof(CompiledRuntime.RoundF64))
    ];

    private static BindingDescriptor Pure(
        string id,
        IReadOnlyList<SandboxType> parameters,
        SandboxType returnType,
        Func<IReadOnlyList<SandboxValue>, SandboxValue> invoke,
        string compiledMethod)
        => new(
            id,
            SemVersion.One,
            parameters,
            returnType,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(2),
            AuditLevel.None,
            BindingSafety.PureIntrinsic,
            (_, args, _) => ValueTask.FromResult(invoke(args)),
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, compiledMethod));
}
