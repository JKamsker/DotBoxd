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

        return CreateDescriptor(
            id,
            parameters,
            returnType,
            effects,
            capability,
            IsTaskLike(interfaceMethod.ReturnType),
            (context, args, cancellationToken) =>
                InvokeAsync(context, args, cancellationToken, id, capability, effects, targetMethod, target, payloadType));
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
                    factoryTargetMethod,
                    factoryTarget,
                    handleInterfaceMethod,
                    payloadType));
    }

    public static Type? UnwrapReturnType(Type type)
    {
        if (type == typeof(void) || type == typeof(Task) || type == typeof(ValueTask))
        {
            return null;
        }

        if ((IsGenericTask(type) || IsGenericValueTask(type)) && type.GetGenericArguments() is [var payload])
        {
            return payload;
        }

        return type;
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
        MethodInfo targetMethod,
        object target,
        Type? payloadType)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startedAt = DateTimeOffset.UtcNow;
        var values = ConvertArguments(targetMethod, args);
        var result = targetMethod.Invoke(target, values);
        var payload = await AwaitReturnAsync(result, targetMethod.ReturnType).ConfigureAwait(false);
        WriteAudit(context, bindingId, capability, effects, startedAt, values);
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
        MethodInfo factoryTargetMethod,
        object factoryTarget,
        MethodInfo handleInterfaceMethod,
        Type? payloadType)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startedAt = DateTimeOffset.UtcNow;
        var factoryArgumentCount = factoryInterfaceMethod.GetParameters().Length;
        var factoryValues = ConvertArguments(factoryTargetMethod, args.Take(factoryArgumentCount).ToArray());
        var handle = factoryTargetMethod.Invoke(factoryTarget, factoryValues)
            ?? throw new InvalidOperationException($"Host service factory '{factoryInterfaceMethod.Name}' returned null.");
        var handleValues = ConvertArguments(handleInterfaceMethod, args.Skip(factoryArgumentCount).ToArray());
        var result = handleInterfaceMethod.Invoke(handle, handleValues);
        var payload = await AwaitReturnAsync(result, handleInterfaceMethod.ReturnType).ConfigureAwait(false);
        WriteAudit(context, bindingId, capability, effects, startedAt, factoryValues.Concat(handleValues).ToArray());
        return payloadType is null
            ? SandboxValue.Unit
            : KernelRpcMarshaller.ToSandboxValue(payload, payloadType);
    }

    private static object?[] ConvertArguments(MethodInfo targetMethod, IReadOnlyList<SandboxValue> args)
    {
        var parameters = targetMethod.GetParameters();
        var values = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            values[i] = KernelRpcMarshaller.FromSandboxValue(args[i], parameters[i].ParameterType);
        }

        return values;
    }

    private static async ValueTask<object?> AwaitReturnAsync(object? result, Type returnType)
    {
        if (returnType == typeof(void) || result is null)
        {
            return null;
        }

        if (returnType == typeof(ValueTask))
        {
            await ((ValueTask)result).ConfigureAwait(false);
            return null;
        }

        if (returnType == typeof(Task))
        {
            await ((Task)result).ConfigureAwait(false);
            return null;
        }

        if (IsGenericValueTask(returnType))
        {
            var task = (Task)returnType.GetMethod(nameof(ValueTask<int>.AsTask))!.Invoke(result, null)!;
            await task.ConfigureAwait(false);
            return task.GetType().GetProperty(nameof(Task<int>.Result))!.GetValue(task);
        }

        if (IsGenericTask(returnType))
        {
            var task = (Task)result;
            await task.ConfigureAwait(false);
            return task.GetType().GetProperty(nameof(Task<int>.Result))!.GetValue(task);
        }

        return result;
    }

    private static void WriteAudit(
        SandboxContext context,
        string bindingId,
        string capability,
        SandboxEffect effects,
        DateTimeOffset startedAt,
        IReadOnlyList<object?> values)
    {
        var resourceId = values.Count > 0 && values[0] is string id ? $"entity:{id}" : bindingId;
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

    private static bool IsGenericTask(Type type)
        => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>);

    private static bool IsGenericValueTask(Type type)
        => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>);

    private static bool IsTaskLike(Type type)
        => type == typeof(Task) ||
           type == typeof(ValueTask) ||
           IsGenericTask(type) ||
           IsGenericValueTask(type);

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
