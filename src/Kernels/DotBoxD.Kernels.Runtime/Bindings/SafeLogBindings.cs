using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Runtime.Bindings;

public static partial class SafeLogBindings
{
    public static BindingDescriptor Info { get; } = Create("log.info", "info");

    public static BindingDescriptor Warn { get; } = Create("log.warn", "warn");

    private static BindingDescriptor Create(string id, string level)
    {
        var invoker = new SafeLogInvoker(id, level);
        return new BindingDescriptor(
            id,
            SemVersion.One,
            [SandboxType.String],
            SandboxType.Unit,
            SandboxEffect.Cpu | SandboxEffect.Audit,
            "log.write",
            BindingCostModel.Fixed(2),
            AuditLevel.PerCall,
            BindingSafety.SideEffectingExternal,
            invoker.Invoke,
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));
    }

    private static void Write(SandboxContext context, string bindingId, string level, string message)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var sanitized = AuditTextSanitizer.SanitizeAndRedact(message);
        context.ChargeLogEvent(sanitized);
        context.ChargeStringAllocation(sanitized.Length);
        var timestamp = context.AuditTimestamp();
        context.Audit.Write(new SandboxAuditEvent(
            context.RunId,
            "SandboxLog",
            timestamp,
            true,
            BindingId: bindingId,
            CapabilityId: "log.write",
            Effect: SandboxEffect.Audit,
            ResourceId: $"log:{level}",
            Message: sanitized,
            Fields: context.BindingAuditFields("log", startedAt)));
    }

    private sealed class SafeLogInvoker(string id, string level) : IOneArgumentBindingInvoker
    {
        public ValueTask<SandboxValue> Invoke(
            SandboxContext context,
            IReadOnlyList<SandboxValue> args,
            CancellationToken cancellationToken)
            => Invoke(context, args[0], cancellationToken);

        public ValueTask<SandboxValue> Invoke(
            SandboxContext context,
            SandboxValue arg0,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Write(context, id, level, ((StringValue)arg0).Value);
            return ValueTask.FromResult(SandboxValue.Unit);
        }
    }
}
