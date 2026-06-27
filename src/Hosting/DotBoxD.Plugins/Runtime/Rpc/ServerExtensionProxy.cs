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
        var arguments = new SandboxValue[method.PayloadParameterTypes.Length];
        for (var i = 0; i < method.PayloadParameterTypes.Length; i++)
        {
            arguments[i] = KernelRpcMarshaller.ToSandboxValue(args?[i], method.PayloadParameterTypes[i]);
        }

        return method.Materialize(_kernel.InvokeServerExtensionAsync(arguments, method.CancellationToken(args)));
    }

    private static Func<ValueTask<SandboxValue>, object?> CreateMaterializer(Type returnType)
    {
        if (returnType == typeof(void))
        {
            return pending =>
            {
                ConsumeUnit(pending.AsTask().GetAwaiter().GetResult());
                return null;
            };
        }

        if (returnType == typeof(Task))
        {
            return pending => InvokeTaskAsync(pending);
        }

        if (returnType == typeof(ValueTask))
        {
            return pending => InvokeValueTaskAsync(pending);
        }

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
        if (!serviceType.IsInterface)
        {
            throw new NotSupportedException("Server extension proxy service type must be an interface.");
        }

        var methods = ContractMethods(serviceType).ToArray();
        if (methods.Length != 1)
        {
            throw new NotSupportedException(
                "Server extension proxy service type must declare exactly one method.");
        }

        foreach (var method in methods)
        {
            var parameters = method.GetParameters();
            for (var i = 0; i < parameters.Length; i++)
            {
                var parameterType = parameters[i].ParameterType;
                if (IsCancellationToken(parameterType))
                {
                    if (i != parameters.Length - 1)
                    {
                        throw new NotSupportedException(
                            "Server extension proxy cancellation tokens must be the final method parameter.");
                    }

                    continue;
                }

                KernelRpcMarshaller.RejectNullableValueTypesForServerExtension(parameterType);
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

    private static bool IsCancellationToken(Type type)
        => type == typeof(CancellationToken);

    private static object BoxTaskAsync<T>(ValueTask<SandboxValue> pending)
        => InvokeTaskAsync<T>(pending);

    private static object BoxValueTaskAsync<T>(ValueTask<SandboxValue> pending)
        => InvokeValueTaskAsync<T>(pending);

    private static async Task InvokeTaskAsync(ValueTask<SandboxValue> pending)
        => ConsumeUnit(await pending.ConfigureAwait(false));

    private static async ValueTask InvokeValueTaskAsync(ValueTask<SandboxValue> pending)
        => ConsumeUnit(await pending.ConfigureAwait(false));

    private static void ConsumeUnit(SandboxValue value)
    {
        if (value.Type != SandboxType.Unit)
        {
            throw new NotSupportedException(
                $"Server extension value expected '{SandboxType.Unit}' but received '{value.Type}'.");
        }
    }

    private sealed class ServerExtensionMethod
    {
        private readonly int _cancellationTokenIndex;
        private readonly Func<ValueTask<SandboxValue>, object?> _materializer;

        public ServerExtensionMethod(MethodInfo method)
        {
            var parameters = method.GetParameters();
            _cancellationTokenIndex = parameters.Length > 0 &&
                IsCancellationToken(parameters[^1].ParameterType)
                    ? parameters.Length - 1
                    : -1;

            var payloadParameterCount = _cancellationTokenIndex >= 0
                ? _cancellationTokenIndex
                : parameters.Length;
            PayloadParameterTypes = new Type[payloadParameterCount];
            for (var i = 0; i < payloadParameterCount; i++)
            {
                PayloadParameterTypes[i] = parameters[i].ParameterType;
            }

            _materializer = CreateMaterializer(method.ReturnType);
        }

        public Type[] PayloadParameterTypes { get; }

        public CancellationToken CancellationToken(object?[]? args)
        {
            if (_cancellationTokenIndex < 0 ||
                args is null ||
                args.Length <= _cancellationTokenIndex ||
                args[_cancellationTokenIndex] is not CancellationToken cancellationToken)
            {
                return default;
            }

            return cancellationToken;
        }

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
