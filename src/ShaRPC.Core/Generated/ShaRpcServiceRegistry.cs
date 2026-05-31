using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using ShaRPC.Core.Client;
using ShaRPC.Core.Server;

namespace ShaRPC.Core.Generated;

/// <summary>
/// Runtime registry populated by ShaRPC-generated service factories.
/// </summary>
public static class ShaRpcServiceRegistry
{
    private const string GeneratedFactoryTypeName = "ShaRPC.Generated.ShaRpcGenerated";

    private static readonly ConcurrentDictionary<Type, RegisteredService> s_services = new();
    private static readonly ConcurrentDictionary<Assembly, bool> s_registrationAttempts = new();

    /// <summary>
    /// Registers generated factories for a service interface.
    /// </summary>
    public static void Register<TService>(
        Func<IShaRpcClient, TService> proxyFactory,
        Func<object, IServiceDispatcher> dispatcherFactory)
        where TService : class
    {
        if (proxyFactory is null)
        {
            throw new ArgumentNullException(nameof(proxyFactory));
        }

        if (dispatcherFactory is null)
        {
            throw new ArgumentNullException(nameof(dispatcherFactory));
        }

        s_services[typeof(TService)] = new RegisteredService(
            client => proxyFactory(client)!,
            dispatcherFactory);
    }

    /// <summary>
    /// Creates the generated client proxy for <typeparamref name="TService"/>.
    /// </summary>
    public static TService CreateProxy<TService>(IShaRpcClient client)
        where TService : class =>
        (TService)CreateProxy(typeof(TService), client);

    /// <summary>
    /// Creates the generated client proxy for <paramref name="serviceInterface"/>.
    /// </summary>
    public static object CreateProxy(Type serviceInterface, IShaRpcClient client)
    {
        if (client is null)
        {
            throw new ArgumentNullException(nameof(client));
        }
        var registration = Resolve(serviceInterface);
        return registration.CreateProxy(client);
    }

    /// <summary>
    /// Creates the generated server dispatcher for <paramref name="implementation"/>.
    /// </summary>
    public static IServiceDispatcher CreateDispatcher<TService>(TService implementation)
        where TService : class =>
        CreateDispatcher(typeof(TService), implementation);

    /// <summary>
    /// Creates the generated server dispatcher for <paramref name="implementation"/>.
    /// </summary>
    public static IServiceDispatcher CreateDispatcher(Type serviceInterface, object implementation)
    {
        if (implementation is null)
        {
            throw new ArgumentNullException(nameof(implementation));
        }
        var registration = Resolve(serviceInterface);
        if (!serviceInterface.IsInstanceOfType(implementation))
        {
            throw new ArgumentException(
                $"{implementation.GetType()} does not implement {FormatType(serviceInterface)}.",
                nameof(implementation));
        }

        return registration.CreateDispatcher(implementation);
    }

    private static RegisteredService Resolve(Type serviceInterface)
    {
        if (serviceInterface is null)
        {
            throw new ArgumentNullException(nameof(serviceInterface));
        }
        if (!serviceInterface.IsInterface)
        {
            throw new ArgumentException(
                $"Service type must be an interface. Received {FormatType(serviceInterface)}.",
                nameof(serviceInterface));
        }

        if (s_services.TryGetValue(serviceInterface, out var registration))
        {
            return registration;
        }

        var generatedTypeFound = EnsureGeneratedRegistration(serviceInterface);
        if (s_services.TryGetValue(serviceInterface, out registration))
        {
            return registration;
        }

        var assemblyName = serviceInterface.Assembly.GetName().Name ?? serviceInterface.Assembly.FullName;
        var reason = generatedTypeFound
            ? "the generated registry in that assembly did not register this service"
            : "no ShaRPC generated registry type was found in that assembly";
        throw new InvalidOperationException(
            $"No ShaRPC generated factory is registered for {FormatType(serviceInterface)}: {reason}. " +
            "Mark the interface with [ShaRpcService] and ensure the assembly that declares it runs the ShaRPC source generator. " +
            $"Assembly: {assemblyName}.");
    }

    private static bool EnsureGeneratedRegistration(Type serviceInterface)
    {
        var assembly = serviceInterface.Assembly;
        if (!s_registrationAttempts.TryAdd(assembly, true))
        {
            return assembly.GetType(GeneratedFactoryTypeName, throwOnError: false) is not null;
        }

        var generatedType = assembly.GetType(GeneratedFactoryTypeName, throwOnError: false);
        if (generatedType is null)
        {
            return false;
        }

        try
        {
            RuntimeHelpers.RunClassConstructor(generatedType.TypeHandle);
            return true;
        }
        catch (Exception ex)
        {
            s_registrationAttempts.TryRemove(assembly, out _);
            throw new InvalidOperationException(
                $"ShaRPC generated factory registration failed for assembly '{assembly.FullName}'.",
                ex);
        }
    }

    private static string FormatType(Type type) => type.FullName ?? type.Name;

    private sealed class RegisteredService
    {
        public RegisteredService(
            Func<IShaRpcClient, object> proxyFactory,
            Func<object, IServiceDispatcher> dispatcherFactory)
        {
            CreateProxy = proxyFactory;
            CreateDispatcher = dispatcherFactory;
        }

        public Func<IShaRpcClient, object> CreateProxy { get; }

        public Func<object, IServiceDispatcher> CreateDispatcher { get; }
    }
}
