using System.Reflection;
using DotBoxD.Services.Server;
using DotBoxD.Services.Tests.Support;
using Shared;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Core;

/// <summary>
/// Behavioral coverage for the generated runtime registry and assembly catalog. The
/// <c>Shared</c> sample assembly declares <see cref="IGameService"/> / <see cref="IPlayerNotifications"/>
/// with <c>[RpcService]</c> and runs the DotBoxD source generator, so these public registry
/// entry points resolve real generated proxy/dispatcher factories without any reflection hacks.
/// </summary>
public sealed partial class GeneratedServiceRegistryCoverageTests
{
    private static Assembly SharedAssembly => typeof(IGameService).Assembly;

    [Fact]
    public void GetServices_ForGeneratedAssembly_ContainsRegisteredServices()
    {
        var services = GeneratedServiceRegistry.GetServices(SharedAssembly);

        Assert.NotEmpty(services);
        Assert.Contains(services, s => s.ServiceType == typeof(IGameService));
        Assert.Contains(services, s => s.ServiceType == typeof(IPlayerNotifications));
        var game = Assert.Single(services, s => s.ServiceType == typeof(IGameService));
        Assert.Equal("IGameService", game.ServiceName);
        // The generated proxy type implements the service interface and the dispatcher type is a
        // generated IServiceDispatcher.
        Assert.True(typeof(IGameService).IsAssignableFrom(game.ProxyType));
        Assert.True(typeof(IServiceDispatcher).IsAssignableFrom(game.DispatcherType));
    }

    [Fact]
    public void GetServices_ForGeneratedAssembly_IsCachedAndStable()
    {
        // The second call hits the ConcurrentDictionary GetOrAdd cache in the catalog and must
        // return the very same list instance.
        var first = GeneratedServiceRegistry.GetServices(SharedAssembly);
        var second = GeneratedServiceRegistry.GetServices(SharedAssembly);

        Assert.Same(first, second);
    }

    [Fact]
    public void GetServices_NullAssembly_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => GeneratedServiceRegistry.GetServices((Assembly)null!));
    }

    [Fact]
    public void GetServices_ForAssemblyWithoutGeneratedRegistry_IsEmpty()
    {
        // The test assembly itself runs no DotBoxD generator, so its catalog is empty (and cached).
        var assembly = typeof(GeneratedServiceRegistryCoverageTests).Assembly;

        var services = GeneratedServiceRegistry.GetServices(assembly);

        Assert.Empty(services);
        Assert.Same(services, GeneratedServiceRegistry.GetServices(assembly));
    }

    [Fact]
    public void GetServices_AssemblyEnumeration_AggregatesAcrossAssemblies()
    {
        var combined = GeneratedServiceRegistry.GetServices(
            new[] { SharedAssembly, typeof(GeneratedServiceRegistryCoverageTests).Assembly });

        Assert.Contains(combined, s => s.ServiceType == typeof(IGameService));
        Assert.Contains(combined, s => s.ServiceType == typeof(IPlayerNotifications));
    }

    [Fact]
    public void GetServices_NullAssemblyEnumeration_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => GeneratedServiceRegistry.GetServices((IEnumerable<Assembly>)null!));
    }

    [Fact]
    public void GetService_Generic_ReturnsMetadataMatchingTypedLookup()
    {
        var typed = GeneratedServiceRegistry.GetService<IGameService>();
        var byType = GeneratedServiceRegistry.GetService(typeof(IGameService));

        Assert.Equal(typeof(IGameService), typed.ServiceType);
        Assert.Equal("IGameService", typed.ServiceName);
        Assert.Equal(typed, byType);
    }

    [Fact]
    public void GetService_UnknownGeneratedInterface_ThrowsInvalidOperationWithGuidance()
    {
        // This interface lives in the test assembly, which has no DotBoxD generated registry type,
        // so resolution must fail with the diagnostic that points at [RpcService] + the generator.
        var ex = Assert.Throws<InvalidOperationException>(
            () => GeneratedServiceRegistry.GetService(typeof(IUngeneratedCoverageService)));

        Assert.Contains("No DotBoxD generated factory is registered", ex.Message);
        Assert.Contains("[RpcService]", ex.Message);
        Assert.Contains("source generator", ex.Message);
    }

    [Fact]
    public void CreateProxy_Typed_ReturnsProxyImplementingInterface()
    {
        var invoker = new RecordingInvoker();

        var proxy = GeneratedServiceRegistry.CreateProxy<IGameService>(invoker);

        Assert.NotNull(proxy);
        Assert.IsAssignableFrom<IGameService>(proxy);
    }

    [Fact]
    public async Task CreateProxy_ByType_ProducesProxyThatRoutesCallsThroughInvoker()
    {
        var invoker = new RecordingInvoker();

        var proxy = (IGameService)GeneratedServiceRegistry.CreateProxy(typeof(IGameService), invoker);
        var status = await proxy.GetServerStatusAsync().WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal("from-invoker", status.Version);
        // The generated proxy must forward the wire service name to the invoker.
        Assert.Equal("IGameService", invoker.LastService);
    }

    [Fact]
    public void CreateProxy_NullInvoker_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => GeneratedServiceRegistry.CreateProxy(typeof(IGameService), null!));
    }

    [Fact]
    public void CreateProxy_NonInterfaceType_ThrowsArgumentException()
    {
        // Resolve rejects non-interface service types regardless of generated state.
        var ex = Assert.Throws<ArgumentException>(
            () => GeneratedServiceRegistry.CreateProxy(typeof(TestGameService), new RecordingInvoker()));

        Assert.Contains("must be an interface", ex.Message);
    }

    [Fact]
    public void CreateDispatcher_Typed_ReturnsDispatcherForServiceName()
    {
        var dispatcher = GeneratedServiceRegistry.CreateDispatcher<IGameService>(new TestGameService());

        Assert.Equal("IGameService", dispatcher.ServiceName);
    }

    [Fact]
    public void CreateDispatcher_ByType_ReturnsDispatcher()
    {
        var dispatcher = GeneratedServiceRegistry.CreateDispatcher(typeof(IGameService), new TestGameService());

        Assert.IsAssignableFrom<IServiceDispatcher>(dispatcher);
        Assert.Equal("IGameService", dispatcher.ServiceName);
    }

    [Fact]
    public void CreateDispatcher_NullImplementation_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => GeneratedServiceRegistry.CreateDispatcher(typeof(IGameService), null!));
    }

    [Fact]
    public void CreateDispatcher_ImplementationNotAssignable_ThrowsArgumentException()
    {
        // The implementation does not implement the requested interface, so dispatcher creation must
        // reject it with a descriptive ArgumentException.
        var ex = Assert.Throws<ArgumentException>(
            () => GeneratedServiceRegistry.CreateDispatcher(typeof(IGameService), new object()));

        Assert.Contains("does not implement", ex.Message);
    }

    [Fact]
    public void RegisterServices_FromGeneratedAssembly_PublishesToSink()
    {
        var sink = new RecordingServiceSink();

        GeneratedServiceRegistry.RegisterServices(new[] { SharedAssembly }, sink);

        Assert.Contains(typeof(IGameService), sink.ServiceTypes);
        Assert.Contains(typeof(IPlayerNotifications), sink.ServiceTypes);
    }

    [Fact]
    public void RegisterServices_NullAssemblies_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => GeneratedServiceRegistry.RegisterServices(null!, new RecordingServiceSink()));
    }

    [Fact]
    public void RegisterServices_NullSink_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => GeneratedServiceRegistry.RegisterServices(new[] { SharedAssembly }, (IRpcServiceRegistrationSink)null!));
    }

    [Fact]
    public void RegisterGeneratedServices_FromGeneratedAssembly_PublishesProxyAndDispatcher()
    {
        var sink = new RecordingGeneratedSink();

        GeneratedServiceRegistry.RegisterGeneratedServices(new[] { SharedAssembly }, sink);

        Assert.Contains(typeof(IGameService), sink.ServiceTypes);
        Assert.Contains(typeof(IPlayerNotifications), sink.ServiceTypes);
    }

    [Fact]
    public void RegisterGeneratedServices_NullAssemblies_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => GeneratedServiceRegistry.RegisterGeneratedServices(null!, new RecordingGeneratedSink()));
    }

    [Fact]
    public void RegisterGeneratedServices_NullSink_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => GeneratedServiceRegistry.RegisterGeneratedServices(
                new[] { SharedAssembly },
                (IRpcGeneratedServiceRegistrationSink)null!));
    }

    [Fact]
    public void RegisterServices_NullAssemblyForCatalog_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => GeneratedServiceRegistry.RegisterServices(
                (Assembly)null!,
                GeneratedServiceRegistry.GetServices(SharedAssembly)));
    }

    [Fact]
    public void RegisterServices_NullServicesForCatalog_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => GeneratedServiceRegistry.RegisterServices(SharedAssembly, null!));
    }

    [Fact]
    public void Register_NullProxyFactory_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            GeneratedServiceRegistry.Register<ICustomRegisteredService>(
                null!,
                _ => new CustomDispatcher(),
                ValidCustomService()));
    }

    [Fact]
    public void Register_NullDispatcherFactory_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            GeneratedServiceRegistry.Register<ICustomRegisteredService>(
                _ => new CustomProxy(),
                null!,
                ValidCustomService()));
    }

}
