using DotBoxD.Services.Server;

namespace DotBoxD.Services.Generated;

internal sealed class RegisteredService
{
    public RegisteredService(
        Func<IRpcInvoker, object> proxyFactory,
        Func<object, IServiceDispatcher> dispatcherFactory,
        GeneratedService service,
        long version)
    {
        CreateProxy = proxyFactory;
        CreateDispatcher = dispatcherFactory;
        Service = service;
        Version = version;
    }

    public Func<IRpcInvoker, object> CreateProxy { get; }

    public Func<object, IServiceDispatcher> CreateDispatcher { get; }

    public GeneratedService Service { get; }

    public long Version { get; }
}
