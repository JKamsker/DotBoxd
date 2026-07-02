using DotBoxD.Services.Server;

namespace DotBoxD.Services.Generated;

internal sealed class RegisteredService
{
    private readonly Func<IRpcInvoker, object> _proxyFactory;
    private readonly Func<object, IServiceDispatcher> _dispatcherFactory;

    public RegisteredService(
        Func<IRpcInvoker, object> proxyFactory,
        Func<object, IServiceDispatcher> dispatcherFactory,
        GeneratedService service,
        long version)
    {
        _proxyFactory = proxyFactory;
        _dispatcherFactory = dispatcherFactory;
        Service = service;
        Version = version;
    }

    public GeneratedService Service { get; }

    public long Version { get; }

    public object CreateProxy(IRpcInvoker invoker) =>
        _proxyFactory(invoker) ??
        throw new InvalidOperationException(
            $"Generated proxy factory for {FormatType(Service.ServiceType)} returned null.");

    public IServiceDispatcher CreateDispatcher(object implementation) =>
        _dispatcherFactory(implementation) ??
        throw new InvalidOperationException(
            $"Generated dispatcher factory for {FormatType(Service.ServiceType)} returned null.");

    private static string FormatType(Type type) => type.FullName ?? type.Name;
}
