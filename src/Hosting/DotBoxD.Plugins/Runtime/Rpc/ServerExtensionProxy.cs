using System.Collections.Concurrent;
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
    private static readonly ConcurrentDictionary<MethodInfo, ServerExtensionMethod> MethodCache = new();
    private static readonly MethodInfo BoxTaskAsyncMethod =
        typeof(ServerExtensionProxy).GetMethod(nameof(BoxTaskAsync), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo BoxValueTaskAsyncMethod =
        typeof(ServerExtensionProxy).GetMethod(nameof(BoxValueTaskAsync), BindingFlags.Static | BindingFlags.NonPublic)!;

    private InstalledKernel _kernel = null!;

    public static TService Create<TService>(InstalledKernel kernel) where TService : class
    {
        ArgumentNullException.ThrowIfNull(kernel);
        ValidateServiceContract(typeof(TService));
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

        var method = MethodCache.GetOrAdd(targetMethod, static target => new ServerExtensionMethod(target));
        var arguments = new SandboxValue[method.ParameterTypes.Length];
        for (var i = 0; i < method.ParameterTypes.Length; i++)
        {
            arguments[i] = KernelRpcMarshaller.ToSandboxValue(args?[i], method.ParameterTypes[i]);
        }

        return method.Materialize(_kernel.InvokeServerExtensionAsync(arguments));
    }

    private static Func<ValueTask<SandboxValue>, object?> CreateMaterializer(Type returnType)
    {
        if (returnType.IsGenericType)
        {
            var definition = returnType.GetGenericTypeDefinition();
            if (definition == typeof(Task<>))
            {
                var inner = returnType.GetGenericArguments()[0];
                return (Func<ValueTask<SandboxValue>, object?>)BoxTaskAsyncMethod
                    .MakeGenericMethod(inner)
                    .CreateDelegate(typeof(Func<ValueTask<SandboxValue>, object?>));
            }

            if (definition == typeof(ValueTask<>))
            {
                var inner = returnType.GetGenericArguments()[0];
                return (Func<ValueTask<SandboxValue>, object?>)BoxValueTaskAsyncMethod
                    .MakeGenericMethod(inner)
                    .CreateDelegate(typeof(Func<ValueTask<SandboxValue>, object?>));
            }
        }

        return pending =>
        {
            var result = pending.AsTask().GetAwaiter().GetResult();
            return KernelRpcMarshaller.FromSandboxValue(result, returnType);
        };
    }

    private static void ValidateServiceContract(Type serviceType)
    {
        foreach (var method in ContractMethods(serviceType))
        {
            foreach (var parameter in method.GetParameters())
            {
                KernelRpcMarshaller.RejectNullableValueTypesForServerExtension(parameter.ParameterType);
            }

            if (UnwrapReturnType(method.ReturnType) is { } payloadType)
            {
                KernelRpcMarshaller.RejectNullableValueTypesForServerExtension(payloadType);
            }
        }
    }

    private static IEnumerable<MethodInfo> ContractMethods(Type serviceType)
    {
        var seen = new HashSet<MethodInfo>();
        foreach (var method in serviceType.GetMethods())
        {
            if (seen.Add(method))
            {
                yield return method;
            }
        }

        foreach (var inherited in serviceType.GetInterfaces())
        {
            foreach (var method in inherited.GetMethods())
            {
                if (seen.Add(method))
                {
                    yield return method;
                }
            }
        }
    }

    private static Type? UnwrapReturnType(Type type)
    {
        if (type == typeof(void) || type == typeof(Task) || type == typeof(ValueTask))
        {
            return null;
        }

        if (type.IsGenericType &&
            type.GetGenericTypeDefinition() is { } definition &&
            (definition == typeof(Task<>) || definition == typeof(ValueTask<>)))
        {
            return type.GetGenericArguments()[0];
        }

        return type;
    }

    private static object BoxTaskAsync<T>(ValueTask<SandboxValue> pending)
        => InvokeTaskAsync<T>(pending);

    private static object BoxValueTaskAsync<T>(ValueTask<SandboxValue> pending)
        => InvokeValueTaskAsync<T>(pending);

    private sealed class ServerExtensionMethod
    {
        private readonly Func<ValueTask<SandboxValue>, object?> _materializer;

        public ServerExtensionMethod(MethodInfo method)
        {
            ParameterTypes = method.GetParameters()
                .Select(static parameter => parameter.ParameterType)
                .ToArray();
            _materializer = CreateMaterializer(method.ReturnType);
        }

        public Type[] ParameterTypes { get; }

        public object? Materialize(ValueTask<SandboxValue> pending)
            => _materializer(pending);
    }

    private static async Task<T> InvokeTaskAsync<T>(ValueTask<SandboxValue> pending)
    {
        var result = await pending.ConfigureAwait(false);
        return (T)KernelRpcMarshaller.FromSandboxValue(result, typeof(T))!;
    }

    private static async ValueTask<T> InvokeValueTaskAsync<T>(ValueTask<SandboxValue> pending)
    {
        var result = await pending.ConfigureAwait(false);
        return (T)KernelRpcMarshaller.FromSandboxValue(result, typeof(T))!;
    }
}
