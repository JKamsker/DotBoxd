using System.Reflection;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Plugins.Runtime.Rpc;

/// <summary>
/// A runtime proxy that implements a server-extension interface by marshaling each call through an
/// installed batch kernel. Arguments are converted to sandbox values, the verified IR runs request/
/// response via <see cref="InstalledKernel.InvokeServerExtensionAsync"/>, and the result is marshaled
/// back to the method's return type, so
/// <c>server.ServerExtension&lt;IMonsterKiller&gt;().KillMonsters(ids)</c> returns real C# objects.
/// The service is expected to declare a single batch method; synchronous, <c>Task&lt;T&gt;</c>, and
/// <c>ValueTask&lt;T&gt;</c> return shapes are supported.
/// </summary>
public class ServerExtensionProxy : DispatchProxy
{
    private InstalledKernel _kernel = null!;

    public static TService Create<TService>(InstalledKernel kernel) where TService : class
    {
        ArgumentNullException.ThrowIfNull(kernel);
        var proxy = Create<TService, ServerExtensionProxy>();
        ((ServerExtensionProxy)(object)proxy!)._kernel = kernel;
        return proxy!;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is null)
        {
            throw new NotSupportedException("Server extension proxy received a null method.");
        }

        var parameters = targetMethod.GetParameters();
        var arguments = new SandboxValue[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            arguments[i] = KernelRpcMarshaller.ToSandboxValue(args?[i], parameters[i].ParameterType);
        }

        var result = _kernel.InvokeServerExtensionAsync(arguments).AsTask().GetAwaiter().GetResult();
        return Materialize(targetMethod.ReturnType, result);
    }

    private static object? Materialize(Type returnType, SandboxValue result)
    {
        // Only Task<T>/ValueTask<T> unwrap to an awaitable; every other return type (including
        // List<T>) is marshaled whole.
        if (returnType.IsGenericType)
        {
            var definition = returnType.GetGenericTypeDefinition();
            if (definition == typeof(Task<>))
            {
                var inner = returnType.GetGenericArguments()[0];
                var value = KernelRpcMarshaller.FromSandboxValue(result, inner);
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, [value]);
            }

            if (definition == typeof(ValueTask<>))
            {
                var inner = returnType.GetGenericArguments()[0];
                return Activator.CreateInstance(returnType, KernelRpcMarshaller.FromSandboxValue(result, inner));
            }
        }

        return KernelRpcMarshaller.FromSandboxValue(result, returnType);
    }
}
