using System.Collections.Concurrent;
using System.Reflection;
using DotBoxd.Services.Server;

namespace DotBoxd.Services.Generated;

/// <summary>
/// Runtime registry populated by DotBoxd-generated service factories.
/// </summary>
public static class DotBoxdServiceRegistry
{
    private static readonly ConcurrentDictionary<Type, RegisteredService> s_services = new();

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
            new DotBoxdGeneratedService(
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
        DotBoxdGeneratedService service)
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

        s_services[typeof(TService)] = new RegisteredService(
            invoker => proxyFactory(invoker)!,
            dispatcherFactory,
            service);
    }

    /// <summary>
    /// Gets generated metadata for <typeparamref name="TService"/>.
    /// </summary>
    public static DotBoxdGeneratedService GetService<TService>()
        where TService : class =>
        GetService(typeof(TService));

    /// <summary>
    /// Gets generated metadata for <paramref name="serviceInterface"/>.
    /// </summary>
    public static DotBoxdGeneratedService GetService(Type serviceInterface) =>
        Resolve(serviceInterface).Service;

    /// <summary>
    /// Gets generated service metadata from <paramref name="assembly"/> without scanning its types.
    /// </summary>
    public static IReadOnlyList<DotBoxdGeneratedService> GetServices(Assembly assembly)
    {
        if (assembly is null)
        {
            throw new ArgumentNullException(nameof(assembly));
        }

        return DotBoxdGeneratedAssemblyCatalog.GetServices(assembly);
    }

    /// <summary>
    /// Gets generated service metadata from multiple assemblies without scanning their types.
    /// </summary>
    public static IReadOnlyList<DotBoxdGeneratedService> GetServices(IEnumerable<Assembly> assemblies)
    {
        if (assemblies is null)
        {
            throw new ArgumentNullException(nameof(assemblies));
        }

        var services = new List<DotBoxdGeneratedService>();
        foreach (var assembly in assemblies)
        {
            services.AddRange(GetServices(assembly));
        }

        return services;
    }

    /// <summary>
    /// Registers generated service metadata for <paramref name="assembly"/>.
    /// </summary>
    public static void RegisterServices(Assembly assembly, IReadOnlyList<DotBoxdGeneratedService> services)
    {
        if (assembly is null)
            throw new ArgumentNullException(nameof(assembly));

        if (services is null)
            throw new ArgumentNullException(nameof(services));

        DotBoxdGeneratedAssemblyCatalog.PublishServices(assembly, services);
    }

    /// <summary>
    /// Adds generated service proxy registrations from multiple assemblies to <paramref name="sink"/>.
    /// </summary>
    public static void RegisterServices(
        IEnumerable<Assembly> assemblies,
        IDotBoxdServiceRegistrationSink sink)
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
            DotBoxdGeneratedAssemblyCatalog.RegisterServices(assembly, sink);
        }
    }

    /// <summary>
    /// Adds generated service, proxy, and dispatcher registrations from multiple assemblies to <paramref name="sink"/>.
    /// </summary>
    public static void RegisterGeneratedServices(
        IEnumerable<Assembly> assemblies,
        IDotBoxdGeneratedServiceRegistrationSink sink)
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
            DotBoxdGeneratedAssemblyCatalog.RegisterGeneratedServices(assembly, sink);
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
        if (invoker is null)
        {
            throw new ArgumentNullException(nameof(invoker));
        }
        var registration = Resolve(serviceInterface);
        return registration.CreateProxy(invoker);
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

        var generatedTypeFound = DotBoxdGeneratedAssemblyCatalog.EnsureRegistered(serviceInterface.Assembly);
        if (s_services.TryGetValue(serviceInterface, out registration))
        {
            return registration;
        }

        var assemblyName = serviceInterface.Assembly.GetName().Name ?? serviceInterface.Assembly.FullName;
        var reason = generatedTypeFound
            ? "the generated registry in that assembly did not register this service"
            : "no DotBoxd generated registry type was found in that assembly";
        throw new InvalidOperationException(
            $"No DotBoxd generated factory is registered for {FormatType(serviceInterface)}: {reason}. " +
            "Mark the interface with [DotBoxdService] and ensure the assembly that declares it runs the DotBoxd source generator. " +
            $"Assembly: {assemblyName}.");
    }

    private static void ValidateService<TService>(DotBoxdGeneratedService service)
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

    private sealed class RegisteredService
    {
        public RegisteredService(
            Func<IRpcInvoker, object> proxyFactory,
            Func<object, IServiceDispatcher> dispatcherFactory,
            DotBoxdGeneratedService service)
        {
            CreateProxy = proxyFactory;
            CreateDispatcher = dispatcherFactory;
            Service = service;
        }

        public Func<IRpcInvoker, object> CreateProxy { get; }

        public Func<object, IServiceDispatcher> CreateDispatcher { get; }

        public DotBoxdGeneratedService Service { get; }
    }
}
