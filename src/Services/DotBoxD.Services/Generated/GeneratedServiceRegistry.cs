using System.Collections.Concurrent;
using System.Reflection;
using DotBoxD.Services.Server;

namespace DotBoxD.Services.Generated;

/// <summary>
/// Runtime registry populated by DotBoxD-generated service factories.
/// </summary>
public static class GeneratedServiceRegistry
{
    private static readonly ConcurrentDictionary<Type, RegisteredService> s_services = new();
    private static readonly object s_registrationLock = new();
    private static long s_registrationVersion;

    internal static long CurrentRegistrationVersion => Volatile.Read(ref s_registrationVersion);

    /// <summary>
    /// Registers generated factories for a service interface.
    /// </summary>
    public static void Register<TService>(
        Func<IRpcInvoker, TService> proxyFactory,
        Func<object, IServiceDispatcher> dispatcherFactory)
        where TService : class =>
        Register(
            proxyFactory,
            dispatcherFactory,
            new GeneratedService(
                typeof(TService),
                typeof(TService),
                typeof(IServiceDispatcher),
                typeof(TService).Name));

    /// <summary>
    /// Registers generated factories and generated service metadata for a service interface.
    /// </summary>
    public static void Register<TService>(
        Func<IRpcInvoker, TService> proxyFactory,
        Func<object, IServiceDispatcher> dispatcherFactory,
        GeneratedService service)
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

        ValidateService<TService>(service);

        lock (s_registrationLock)
        {
            var version = s_registrationVersion + 1;
            var snapshot = GeneratedServiceCatalogSnapshot.Snapshot(service);
            s_services[typeof(TService)] = new RegisteredService(
                invoker => proxyFactory(invoker)!,
                dispatcherFactory,
                snapshot,
                version);
            Volatile.Write(ref s_registrationVersion, version);
        }
    }

    /// <summary>
    /// Gets generated metadata for <typeparamref name="TService"/>.
    /// </summary>
    public static GeneratedService GetService<TService>()
        where TService : class =>
        GetService(typeof(TService));

    /// <summary>
    /// Gets generated metadata for <paramref name="serviceInterface"/>.
    /// </summary>
    public static GeneratedService GetService(Type serviceInterface) =>
        Resolve(serviceInterface).Service;

    /// <summary>
    /// Gets generated service metadata from <paramref name="assembly"/> without scanning its types.
    /// </summary>
    public static IReadOnlyList<GeneratedService> GetServices(Assembly assembly)
    {
        if (assembly is null)
        {
            throw new ArgumentNullException(nameof(assembly));
        }

        return RpcGeneratedAssemblyCatalog.GetServices(assembly);
    }

    /// <summary>
    /// Gets generated service metadata from multiple assemblies without scanning their types.
    /// </summary>
    public static IReadOnlyList<GeneratedService> GetServices(IEnumerable<Assembly> assemblies)
    {
        if (assemblies is null)
        {
            throw new ArgumentNullException(nameof(assemblies));
        }

        var services = new List<GeneratedService>();
        foreach (var assembly in assemblies)
        {
            services.AddRange(GetServices(assembly));
        }

        return services.AsReadOnly();
    }

    /// <summary>
    /// Registers generated service metadata for <paramref name="assembly"/>.
    /// </summary>
    public static void RegisterServices(Assembly assembly, IReadOnlyList<GeneratedService> services)
    {
        if (assembly is null)
            throw new ArgumentNullException(nameof(assembly));

        if (services is null)
            throw new ArgumentNullException(nameof(services));

        RpcGeneratedAssemblyCatalog.PublishServices(assembly, services);
    }

    /// <summary>
    /// Adds generated service proxy registrations from multiple assemblies to <paramref name="sink"/>.
    /// </summary>
    public static void RegisterServices(
        IEnumerable<Assembly> assemblies,
        IRpcServiceRegistrationSink sink)
    {
        if (assemblies is null)
        {
            throw new ArgumentNullException(nameof(assemblies));
        }

        if (sink is null)
        {
            throw new ArgumentNullException(nameof(sink));
        }

        foreach (var assembly in assemblies)
        {
            RpcGeneratedAssemblyCatalog.RegisterServices(assembly, sink);
        }
    }

    /// <summary>
    /// Adds generated service, proxy, and dispatcher registrations from multiple assemblies to <paramref name="sink"/>.
    /// </summary>
    public static void RegisterGeneratedServices(
        IEnumerable<Assembly> assemblies,
        IRpcGeneratedServiceRegistrationSink sink)
    {
        if (assemblies is null)
        {
            throw new ArgumentNullException(nameof(assemblies));
        }

        if (sink is null)
        {
            throw new ArgumentNullException(nameof(sink));
        }

        foreach (var assembly in assemblies)
        {
            RpcGeneratedAssemblyCatalog.RegisterGeneratedServices(assembly, sink);
        }
    }

    /// <summary>
    /// Creates the generated client proxy for <typeparamref name="TService"/>.
    /// </summary>
    public static TService CreateProxy<TService>(IRpcInvoker invoker)
        where TService : class =>
        (TService)CreateProxy(typeof(TService), invoker);

    /// <summary>
    /// Creates the generated client proxy for <paramref name="serviceInterface"/>.
    /// </summary>
    public static object CreateProxy(Type serviceInterface, IRpcInvoker invoker)
    {
        return CreateProxy(serviceInterface, invoker, out _);
    }

    internal static object CreateProxy(Type serviceInterface, IRpcInvoker invoker, out long registrationVersion)
    {
        if (invoker is null)
        {
            throw new ArgumentNullException(nameof(invoker));
        }
        var registration = Resolve(serviceInterface);
        registrationVersion = registration.Version;
        return registration.CreateProxy(invoker);
    }

    internal static bool IsRegistrationCurrent(
        Type serviceInterface,
        long registrationVersion,
        out long registryVersion)
    {
        registryVersion = CurrentRegistrationVersion;
        return s_services.TryGetValue(serviceInterface, out var registration) &&
            registration.Version == registrationVersion;
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

        var generatedTypeFound = RpcGeneratedAssemblyCatalog.EnsureRegistered(serviceInterface.Assembly);
        if (s_services.TryGetValue(serviceInterface, out registration))
        {
            return registration;
        }

        var assemblyName = serviceInterface.Assembly.GetName().Name ?? serviceInterface.Assembly.FullName;
        var reason = generatedTypeFound
            ? "the generated registry in that assembly did not register this service"
            : "no DotBoxD generated registry type was found in that assembly";
        throw new InvalidOperationException(
            $"No DotBoxD generated factory is registered for {FormatType(serviceInterface)}: {reason}. " +
            "Mark the interface with [RpcService] and ensure the assembly that declares it runs the DotBoxD source generator. " +
            $"Assembly: {assemblyName}.");
    }

    private static void ValidateService<TService>(GeneratedService service)
        where TService : class
    {
        if (service.ServiceType is null)
        {
            throw new ArgumentException("Generated service metadata must include a service type.", nameof(service));
        }
        if (service.ProxyType is null)
        {
            throw new ArgumentException("Generated service metadata must include a proxy type.", nameof(service));
        }
        if (service.DispatcherType is null)
        {
            throw new ArgumentException("Generated service metadata must include a dispatcher type.", nameof(service));
        }
        if (string.IsNullOrEmpty(service.ServiceName))
        {
            throw new ArgumentException("Generated service metadata must include a service name.", nameof(service));
        }
        if (service.ServiceType != typeof(TService))
        {
            throw new ArgumentException(
                $"Generated service metadata describes {FormatType(service.ServiceType)}, " +
                $"but it was registered for {FormatType(typeof(TService))}.",
                nameof(service));
        }
    }

    private static string FormatType(Type type) => type.FullName ?? type.Name;

}
