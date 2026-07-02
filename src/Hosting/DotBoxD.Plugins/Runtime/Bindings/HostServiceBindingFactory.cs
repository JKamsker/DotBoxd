using System.Reflection;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime.Rpc;
using DotBoxD.Shared.HostBindings;

namespace DotBoxD.Hosting.Execution;

internal static partial class HostServiceBindingFactory
{
    public static BindingDescriptor CreateBinding(
        MethodInfo interfaceMethod,
        MethodInfo targetMethod,
        object target,
        HostBindingAttribute binding)
    {
        var payloadType = UnwrapReturnType(interfaceMethod.ReturnType);
        var parameters = interfaceMethod.GetParameters()
            .Select(parameter => ServerExtensionSandboxTypeOf(parameter.ParameterType))
            .ToArray();
        var returnType = payloadType is null ? SandboxType.Unit : ServerExtensionSandboxTypeOf(payloadType);
        var effects = DeclaredEffects(interfaceMethod, returnType, binding);
        var id = HostBindingId(interfaceMethod.DeclaringType!, interfaceMethod, binding);
        var callTarget = new HostServiceCallTarget(targetMethod);

        return CreateDescriptor(
            id,
            parameters,
            returnType,
            effects,
            binding.Capability,
            IsTaskLike(interfaceMethod.ReturnType),
            (context, args, cancellationToken) =>
                InvokeAsync(context, args, cancellationToken, id, binding.Capability, effects, callTarget, target, payloadType));
    }

    public static BindingDescriptor CreateHandleBinding(
        MethodInfo factoryInterfaceMethod,
        MethodInfo factoryTargetMethod,
        object factoryTarget,
        MethodInfo handleInterfaceMethod,
        HostBindingAttribute binding)
    {
        var payloadType = UnwrapReturnType(handleInterfaceMethod.ReturnType);
        var parameters = factoryInterfaceMethod.GetParameters()
            .Concat(handleInterfaceMethod.GetParameters())
            .Select(parameter => ServerExtensionSandboxTypeOf(parameter.ParameterType))
            .ToArray();
        var returnType = payloadType is null ? SandboxType.Unit : ServerExtensionSandboxTypeOf(payloadType);
        var effects = DeclaredEffects(handleInterfaceMethod, returnType, binding);
        var id = HostBindingId(handleInterfaceMethod.DeclaringType!, handleInterfaceMethod, binding);
        var factoryCallTarget = new HostServiceCallTarget(factoryTargetMethod);
        var handleCallTarget = new HostServiceCallTarget(handleInterfaceMethod);

        return CreateDescriptor(
            id,
            parameters,
            returnType,
            effects,
            binding.Capability,
            IsTaskLike(handleInterfaceMethod.ReturnType),
            (context, args, cancellationToken) =>
                InvokeHandleAsync(
                    context,
                    args,
                    cancellationToken,
                    id,
                    binding.Capability,
                    effects,
                    factoryInterfaceMethod,
                    factoryCallTarget,
                    factoryTarget,
                    handleCallTarget,
                payloadType));
    }

    public static Type? UnwrapReturnType(Type type)
        => HostServiceCallTarget.UnwrapReturnType(type);

    private static SandboxType ServerExtensionSandboxTypeOf(Type type)
    {
        KernelRpcMarshaller.RejectNullableValueTypesForServerExtension(type);
        return KernelRpcMarshaller.SandboxTypeOf(type);
    }

    private static BindingDescriptor CreateDescriptor(
        string id,
        IReadOnlyList<SandboxType> parameters,
        SandboxType returnType,
        SandboxEffect effects,
        string capability,
        bool isAsync,
        BindingInvoker binding)
    {
        var safety = (effects & SandboxEffect.HostStateWrite) != SandboxEffect.None
            ? BindingSafety.SideEffectingExternal
            : BindingSafety.ReadOnlyExternal;

        return new BindingDescriptor(
            id,
            SemVersion.One,
            parameters,
            returnType,
            effects,
            capability,
            BindingCostModel.Fixed(BaseFuel(returnType)),
            AuditLevel.PerResource,
            safety,
            binding,
            CompiledBinding.RuntimeStub("DotBoxD.Kernels.Runtime.CompiledRuntime", "CallBinding"),
            GrantValidator: static (_, _) => { })
        {
            IsAsync = isAsync
        };
    }

    private static async ValueTask<SandboxValue> InvokeAsync(
        SandboxContext context,
        IReadOnlyList<SandboxValue> args,
        CancellationToken cancellationToken,
        string bindingId,
        string capability,
        SandboxEffect effects,
        HostServiceCallTarget callTarget,
        object target,
        Type? payloadType)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startedAt = DateTimeOffset.UtcNow;
        var values = ConvertArguments(callTarget.ParameterTypes, args, startIndex: 0);
        var result = callTarget.Invoke(target, values);
        var payload = await callTarget.ReadReturnAsync(result).ConfigureAwait(false);
        WriteAudit(context, bindingId, capability, effects, startedAt, values.Length > 0 ? values[0] : null);
        return payloadType is null
            ? SandboxValue.Unit
            : KernelRpcMarshaller.ToSandboxValue(payload, payloadType);
    }

    private static async ValueTask<SandboxValue> InvokeHandleAsync(
        SandboxContext context,
        IReadOnlyList<SandboxValue> args,
        CancellationToken cancellationToken,
        string bindingId,
        string capability,
        SandboxEffect effects,
        MethodInfo factoryInterfaceMethod,
        HostServiceCallTarget factoryCallTarget,
        object factoryTarget,
        HostServiceCallTarget handleCallTarget,
        Type? payloadType)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startedAt = DateTimeOffset.UtcNow;
        var factoryValues = ConvertArguments(factoryCallTarget.ParameterTypes, args, startIndex: 0);
        var handle = factoryCallTarget.Invoke(factoryTarget, factoryValues)
            ?? throw new InvalidOperationException($"Host service factory '{factoryInterfaceMethod.Name}' returned null.");
        var handleValues = ConvertArguments(handleCallTarget.ParameterTypes, args, factoryCallTarget.ParameterTypes.Length);
        var result = handleCallTarget.Invoke(handle, handleValues);
        var payload = await handleCallTarget.ReadReturnAsync(result).ConfigureAwait(false);
        var auditValue = factoryValues.Length > 0 ? factoryValues[0] : handleValues.Length > 0 ? handleValues[0] : null;
        WriteAudit(context, bindingId, capability, effects, startedAt, auditValue);
        return payloadType is null
            ? SandboxValue.Unit
            : KernelRpcMarshaller.ToSandboxValue(payload, payloadType);
    }

    private static object?[] ConvertArguments(
        Type[] parameterTypes,
        IReadOnlyList<SandboxValue> args,
        int startIndex)
    {
        if (parameterTypes.Length == 0)
        {
            return Array.Empty<object?>();
        }

        var values = new object?[parameterTypes.Length];
        for (var i = 0; i < parameterTypes.Length; i++)
        {
            values[i] = KernelRpcMarshaller.FromSandboxValue(args[startIndex + i], parameterTypes[i]);
        }

        return values;
    }

    private static void WriteAudit(
        SandboxContext context,
        string bindingId,
        string capability,
        SandboxEffect effects,
        DateTimeOffset startedAt,
        object? firstArgument)
    {
        var resourceId = firstArgument is string id ? $"entity:{id}" : bindingId;
        context.Audit.Write(new SandboxAuditEvent(
            context.RunId,
            "BindingCall",
            startedAt,
            true,
            BindingId: bindingId,
            CapabilityId: capability,
            Effect: effects & (SandboxEffect.HostStateRead | SandboxEffect.HostStateWrite),
            ResourceId: resourceId,
            Fields: context.BindingAuditFields("host-service", startedAt)));
    }

    private static bool IsTaskLike(Type type)
        => HostServiceCallTarget.IsTaskLike(type);

    private static SandboxEffect DeclaredEffects(
        MethodInfo method,
        SandboxType returnType,
        HostBindingAttribute binding)
    {
        var effects = binding.Effects;
        if (!effects.ContainsOnlyKnownBits())
        {
            throw new InvalidOperationException(
                $"Host binding method '{method.DeclaringType?.FullName}.{method.Name}' declares unknown effects.");
        }

        var access = effects & (SandboxEffect.HostStateRead | SandboxEffect.HostStateWrite);
        if (access is not SandboxEffect.HostStateRead and not SandboxEffect.HostStateWrite)
        {
            throw new InvalidOperationException(
                $"Host binding method '{method.DeclaringType?.FullName}.{method.Name}' must declare exactly one of HostStateRead or HostStateWrite.");
        }

        var allocates = (effects & SandboxEffect.Alloc) == SandboxEffect.Alloc;
        var returnAllocates = ReturnAllocates(returnType);
        if (allocates != returnAllocates)
        {
            throw new InvalidOperationException(
                returnAllocates
                    ? $"Host binding method '{method.DeclaringType?.FullName}.{method.Name}' must declare Alloc because its return shape allocates."
                    : $"Host binding method '{method.DeclaringType?.FullName}.{method.Name}' must not declare Alloc because its return shape does not allocate.");
        }

        return effects;
    }

    private static bool ReturnAllocates(SandboxType type)
        => HostBindingMetadataRules.ReturnAllocatesSandboxTypeName(type.Name);

    private static long BaseFuel(SandboxType returnType) => ReturnAllocates(returnType) ? 3 : 2;

    private static string HostBindingRoute(Type type, MethodInfo method)
        => HostBindingMetadataRules.BindingId(type.Namespace, type.Name, method.Name);

    private static string HostBindingId(Type type, MethodInfo method, HostBindingAttribute binding)
        => binding.IsAutoBinding ? HostBindingRoute(type, method) : binding.BindingId;
}
