using System.Reflection;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Hosting.Execution;

internal static partial class HostServiceBindingFactory
{
    public static BindingDescriptor CreatePropertyBinding(
        PropertyInfo interfaceProperty,
        MethodInfo targetGetter,
        object target,
        HostBindingAttribute binding)
    {
        var payloadType = UnwrapReturnType(interfaceProperty.PropertyType);
        var returnType = payloadType is null ? SandboxType.Unit : ServerExtensionSandboxTypeOf(payloadType);
        var effects = DeclaredEffects(interfaceProperty, returnType, binding);
        var callTarget = new HostServiceCallTarget(targetGetter);

        return CreateDescriptor(
            binding.BindingId,
            [],
            returnType,
            effects,
            binding.Capability,
            binding.IsAsync,
            (context, args, cancellationToken) =>
                InvokePropertyAsync(context, args, cancellationToken, binding, effects, callTarget, target, payloadType));
    }

    private static async ValueTask<SandboxValue> InvokePropertyAsync(
        SandboxContext context,
        IReadOnlyList<SandboxValue> args,
        CancellationToken cancellationToken,
        HostBindingAttribute binding,
        SandboxEffect effects,
        HostServiceCallTarget callTarget,
        object target,
        Type? payloadType)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (args.Count != 0)
        {
            throw new InvalidOperationException(
                $"Host service property binding '{binding.BindingId}' expects no arguments.");
        }

        var startedAt = DateTimeOffset.UtcNow;
        var result = callTarget.Invoke(target, []);
        var payload = await callTarget.ReadReturnAsync(result).ConfigureAwait(false);
        WriteAudit(context, binding.BindingId, binding.Capability, effects, startedAt, firstArgument: null);
        return payloadType is null
            ? SandboxValue.Unit
            : KernelRpcMarshaller.ToSandboxValue(payload, payloadType);
    }

    private static SandboxEffect DeclaredEffects(
        PropertyInfo property,
        SandboxType returnType,
        HostBindingAttribute binding)
    {
        var effects = binding.Effects;
        if (!effects.ContainsOnlyKnownBits())
        {
            throw new InvalidOperationException(
                $"Host binding property '{property.DeclaringType?.FullName}.{property.Name}' declares unknown effects.");
        }

        var access = effects & (SandboxEffect.HostStateRead | SandboxEffect.HostStateWrite);
        if (access is not SandboxEffect.HostStateRead and not SandboxEffect.HostStateWrite)
        {
            throw new InvalidOperationException(
                $"Host binding property '{property.DeclaringType?.FullName}.{property.Name}' must declare exactly one of HostStateRead or HostStateWrite.");
        }

        var allocates = (effects & SandboxEffect.Alloc) == SandboxEffect.Alloc;
        var returnAllocates = ReturnAllocates(returnType);
        if (allocates != returnAllocates)
        {
            throw new InvalidOperationException(
                returnAllocates
                    ? $"Host binding property '{property.DeclaringType?.FullName}.{property.Name}' must declare Alloc because its return shape allocates."
                    : $"Host binding property '{property.DeclaringType?.FullName}.{property.Name}' must not declare Alloc because its return shape does not allocate.");
        }

        return effects;
    }
}
