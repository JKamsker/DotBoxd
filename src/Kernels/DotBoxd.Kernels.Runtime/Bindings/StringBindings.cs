namespace DotBoxd.Kernels.Runtime;

using DotBoxd.Kernels;

public static class StringBindings
{
    public static IReadOnlyList<BindingDescriptor> All { get; } = [
        Pure(
            "string.length",
            [SandboxType.String],
            SandboxType.I32,
            SandboxEffect.Cpu,
            BindingCostModel.Fixed(1),
            (_, args, _) => ValueTask.FromResult(SandboxValue.FromInt32(String(args[0]).Length)),
            nameof(CompiledRuntime.StringLength)),
        Pure(
            "string.isEmpty",
            [SandboxType.String],
            SandboxType.Bool,
            SandboxEffect.Cpu,
            BindingCostModel.Fixed(1),
            (_, args, _) => ValueTask.FromResult(SandboxValue.FromBool(String(args[0]).Length == 0))),
        Pure(
            "string.substringBudgeted",
            [SandboxType.String, SandboxType.I32, SandboxType.I32],
            SandboxType.String,
            SandboxEffect.Cpu | SandboxEffect.Alloc,
            BindingCostModel.PerByte(2, 1),
            (ctx, args, _) => {
                var text = ctx.CreateChargedSubstring(String(args[0]), I32(args[1]), I32(args[2]));
                return ValueTask.FromResult(SandboxValue.FromString(text));
            }),
        Pure(
            "string.concatBudgeted",
            [SandboxType.String, SandboxType.String],
            SandboxType.String,
            SandboxEffect.Cpu | SandboxEffect.Alloc,
            BindingCostModel.PerByte(2, 1),
            (ctx, args, _) => {
                var text = ctx.CreateChargedStringConcat(
                    String(args[0]),
                    String(args[1]));
                return ValueTask.FromResult(SandboxValue.FromString(text));
            }),
        Pure(
            "string.equals",
            [SandboxType.String, SandboxType.String],
            SandboxType.Bool,
            SandboxEffect.Cpu,
            BindingCostModel.Fixed(2),
            (ctx, args, _) => {
                var left = String(args[0]);
                var right = String(args[1]);
                ctx.ChargeFuel(CheckedCharCount(left, right));
                return ValueTask.FromResult(SandboxValue.FromBool(string.Equals(left, right, StringComparison.Ordinal)));
            }),
        Pure(
            "string.compareOrdinal",
            [SandboxType.String, SandboxType.String],
            SandboxType.I32,
            SandboxEffect.Cpu,
            BindingCostModel.Fixed(2),
            (ctx, args, _) => {
                var left = String(args[0]);
                var right = String(args[1]);
                ctx.ChargeFuel(CheckedCharCount(left, right));
                return ValueTask.FromResult(SandboxValue.FromInt32(Math.Sign(string.CompareOrdinal(left, right))));
            })
    ];

    private static BindingDescriptor Pure(
        string id,
        IReadOnlyList<SandboxType> parameters,
        SandboxType returnType,
        SandboxEffect effects,
        BindingCostModel cost,
        BindingInvoker invoke,
        string compiledMethod = nameof(CompiledRuntime.CallBinding))
        => new(
            id,
            SemVersion.One,
            parameters,
            returnType,
            effects,
            null,
            cost,
            AuditLevel.None,
            BindingSafety.PureIntrinsic,
            invoke,
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, compiledMethod));

    private static string String(SandboxValue value) => ((StringValue)value).Value;

    private static int I32(SandboxValue value) => ((I32Value)value).Value;

    private static long CheckedCharCount(string left, string right)
    {
        try
        {
            return checked((long)left.Length + right.Length);
        }
        catch (OverflowException)
        {
            throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.QuotaExceeded, "string CPU budget exhausted"));
        }
    }
}
