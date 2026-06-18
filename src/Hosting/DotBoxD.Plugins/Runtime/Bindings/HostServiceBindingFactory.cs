using System.Reflection;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Hosting.Execution;

internal static class HostServiceBindingFactory
{
    public static BindingDescriptor CreateBinding(
        MethodInfo interfaceMethod,
        MethodInfo targetMethod,
        object target,
        string capability)
    {
        var payloadType = UnwrapReturnType(interfaceMethod.ReturnType);
        var parameters = interfaceMethod.GetParameters()
            .Select(parameter => KernelRpcMarshaller.SandboxTypeOf(parameter.ParameterType))
            .ToArray();
        var returnType = payloadType is null ? SandboxType.Unit : KernelRpcMarshaller.SandboxTypeOf(payloadType);
        var effects = InferEffects(interfaceMethod, returnType, capability);
        var id = HostBindingRoute(interfaceMethod.DeclaringType!, interfaceMethod);
        var callTarget = new HostServiceCallTarget(targetMethod);

        return CreateDescriptor(
            id,
            parameters,
            returnType,
            effects,
            capability,
            IsTaskLike(interfaceMethod.ReturnType),
            (context, args, cancellationToken) =>
                InvokeAsync(context, args, cancellationToken, id, capability, effects, callTarget, target, payloadType));
    }

    public static BindingDescriptor CreateHandleBinding(
        MethodInfo factoryInterfaceMethod,
        MethodInfo factoryTargetMethod,
        object factoryTarget,
        MethodInfo handleInterfaceMethod,
        string capability)
    {
        var payloadType = UnwrapReturnType(handleInterfaceMethod.ReturnType);
        var parameters = factoryInterfaceMethod.GetParameters()
            .Concat(handleInterfaceMethod.GetParameters())
            .Select(parameter => KernelRpcMarshaller.SandboxTypeOf(parameter.ParameterType))
            .ToArray();
        var returnType = payloadType is null ? SandboxType.Unit : KernelRpcMarshaller.SandboxTypeOf(payloadType);
        var effects = InferEffects(handleInterfaceMethod, returnType, capability);
        var id = HostBindingRoute(handleInterfaceMethod.DeclaringType!, handleInterfaceMethod);
        var factoryCallTarget = new HostServiceCallTarget(factoryTargetMethod);
        var handleCallTarget = new HostServiceCallTarget(handleInterfaceMethod);

        return CreateDescriptor(
            id,
            parameters,
            returnType,
            effects,
            capability,
            IsTaskLike(handleInterfaceMethod.ReturnType),
            (context, args, cancellationToken) =>
                InvokeHandleAsync(
                    context,
                    args,
                    cancellationToken,
                    id,
                    capability,
                    effects,
                    factoryInterfaceMethod,
                    factoryCallTarget,
                    factoryTarget,
                    handleCallTarget,
                    payloadType));
    }

    public static Type? UnwrapReturnType(Type type)
        => HostServiceCallTarget.UnwrapReturnType(type);

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

    private static SandboxEffect InferEffects(MethodInfo method, SandboxType returnType, string capability)
    {
        var effects = SandboxEffect.Cpu;
        if (ReturnAllocates(returnType))
        {
            effects |= SandboxEffect.Alloc;
        }

        return IsWriteMethod(method, capability)
            ? effects | SandboxEffect.HostStateWrite
            : effects | SandboxEffect.HostStateRead;
    }

    private static bool IsWriteMethod(MethodInfo method, string capability)
        => capability.Contains(".write.", StringComparison.Ordinal) ||
           method.Name.StartsWith("Kill", StringComparison.Ordinal) ||
           method.Name.StartsWith("Set", StringComparison.Ordinal) ||
           method.Name.StartsWith("Update", StringComparison.Ordinal) ||
           method.Name.StartsWith("Delete", StringComparison.Ordinal) ||
           method.Name.StartsWith("Add", StringComparison.Ordinal) ||
           method.Name.StartsWith("Remove", StringComparison.Ordinal) ||
           method.Name.StartsWith("Move", StringComparison.Ordinal) ||
           method.Name.StartsWith("Teleport", StringComparison.Ordinal);

    private static bool ReturnAllocates(SandboxType type)
        => type != SandboxType.Unit &&
           type != SandboxType.Bool &&
           type != SandboxType.I32 &&
           type != SandboxType.I64 &&
           type != SandboxType.F64;

    private static long BaseFuel(SandboxType returnType) => ReturnAllocates(returnType) ? 3 : 2;

    private static string HostBindingRoute(Type type, MethodInfo method)
        => "host." + (type.Namespace is null ? type.Name : type.Namespace + "." + type.Name) + "." + method.Name;
}
