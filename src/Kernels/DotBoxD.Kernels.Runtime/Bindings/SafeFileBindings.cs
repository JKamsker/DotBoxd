using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Runtime.Bindings;

public static class SafeFileBindings
{
    public static BindingDescriptor ReadText { get; } = new(
        "file.readText",
        SemVersion.One,
        [SandboxType.SandboxPath],
        SandboxType.String,
        SandboxEffect.Cpu | SandboxEffect.Alloc | SandboxEffect.FileRead,
        "file.read",
        BindingCostModel.Fixed(50),
        AuditLevel.PerResource,
        BindingSafety.ReadOnlyExternal,
        async (context, args, cancellationToken) => {
            var text = await SafeFileSystem.ReadTextAsync(context, ((SandboxPathValue)args[0]).Value, cancellationToken)
                .ConfigureAwait(false);
            return SandboxValue.FromString(text);
        },
        CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)))
    {
        IsAsync = true
    };

    public static BindingDescriptor WriteText { get; } = new(
        "file.writeText",
        SemVersion.One,
        [SandboxType.SandboxPath, SandboxType.String],
        SandboxType.Unit,
        SandboxEffect.Cpu | SandboxEffect.FileWrite | SandboxEffect.Audit,
        "file.write",
        BindingCostModel.Fixed(50),
        AuditLevel.PerResource,
        BindingSafety.SideEffectingExternal,
        async (context, args, cancellationToken) => {
            await SafeFileSystem.WriteTextAsync(
                context,
                ((SandboxPathValue)args[0]).Value,
                ((StringValue)args[1]).Value,
                cancellationToken).ConfigureAwait(false);
            return SandboxValue.Unit;
        },
        CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)))
    {
        IsAsync = true
    };
}
