using System.Buffers;
using System.Reflection;
using DotBoxd.Services;
using DotBoxd.Services.Generated;
using DotBoxd.Services.Serialization;
using DotBoxd.Services.Server;
using DotBoxd.Services.Tests;
using Shared;
using Xunit;

namespace DotBoxd.Services.Tests.Cov.RegistryDiag;

/// <summary>
/// Behavioral coverage for the generated runtime registry and assembly catalog. The
/// <c>Shared</c> sample assembly declares <see cref="IGameService"/> / <see cref="IPlayerNotifications"/>
/// with <c>[DotBoxdService]</c> and runs the DotBoxd source generator, so these public registry
/// entry points resolve real generated proxy/dispatcher factories without any reflection hacks.
/// </summary>
public sealed class DotBoxdServiceRegistryCoverageTests
{
    private static Assembly SharedAssembly => typeof(IGameService).Assembly;

    [Fact]
    public void GetServices_ForGeneratedAssembly_ContainsRegisteredServices()
    {
        var services = DotBoxdServiceRegistry.GetServices(SharedAssembly);

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
        var first = DotBoxdServiceRegistry.GetServices(SharedAssembly);
        var second = DotBoxdServiceRegistry.GetServices(SharedAssembly);

        Assert.Same(first, second);
    }

    [Fact]
    public void GetServices_NullAssembly_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => DotBoxdServiceRegistry.GetServices((Assembly)null!));
    }

    [Fact]
    public void GetServices_ForAssemblyWithoutGeneratedRegistry_IsEmpty()
    {
        // The test assembly itself runs no DotBoxd generator, so its catalog is empty (and cached).
        var assembly = typeof(DotBoxdServiceRegistryCoverageTests).Assembly;

        var services = DotBoxdServiceRegistry.GetServices(assembly);

        Assert.Empty(services);
        Assert.Same(services, DotBoxdServiceRegistry.GetServices(assembly));
    }

    [Fact]
    public void GetServices_AssemblyEnumeration_AggregatesAcrossAssemblies()
    {
        var combined = DotBoxdServiceRegistry.GetServices(
            new[] { SharedAssembly, typeof(DotBoxdServiceRegistryCoverageTests).Assembly });

        Assert.Contains(combined, s => s.ServiceType == typeof(IGameService));
        Assert.Contains(combined, s => s.ServiceType == typeof(IPlayerNotifications));
    }

    [Fact]
    public void GetServices_NullAssemblyEnumeration_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => DotBoxdServiceRegistry.GetServices((IEnumerable<Assembly>)null!));
    }

    [Fact]
    public void GetService_Generic_ReturnsMetadataMatchingTypedLookup()
    {
        var typed = DotBoxdServiceRegistry.GetService<IGameService>();
        var byType = DotBoxdServiceRegistry.GetService(typeof(IGameService));

        Assert.Equal(typeof(IGameService), typed.ServiceType);
        Assert.Equal("IGameService", typed.ServiceName);
        Assert.Equal(typed, byType);
    }

    [Fact]
    public void GetService_UnknownGeneratedInterface_ThrowsInvalidOperationWithGuidance()
    {
        // This interface lives in the test assembly, which has no DotBoxd generated registry type,
        // so resolution must fail with the diagnostic that points at [DotBoxdService] + the generator.
        var ex = Assert.Throws<InvalidOperationException>(
            () => DotBoxdServiceRegistry.GetService(typeof(IUngeneratedCoverageService)));

        Assert.Contains("No DotBoxd generated factory is registered", ex.Message);
        Assert.Contains("[DotBoxdService]", ex.Message);
        Assert.Contains("source generator", ex.Message);
    }

    [Fact]
    public void CreateProxy_Typed_ReturnsProxyImplementingInterface()
    {
        var invoker = new RecordingInvoker();

        var proxy = DotBoxdServiceRegistry.CreateProxy<IGameService>(invoker);

        Assert.NotNull(proxy);
        Assert.IsAssignableFrom<IGameService>(proxy);
    }

    [Fact]
    public async Task CreateProxy_ByType_ProducesProxyThatRoutesCallsThroughInvoker()
    {
        var invoker = new RecordingInvoker();

        var proxy = (IGameService)DotBoxdServiceRegistry.CreateProxy(typeof(IGameService), invoker);
        var status = await proxy.GetServerStatusAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("from-invoker", status.Version);
        // The generated proxy must forward the wire service name to the invoker.
        Assert.Equal("IGameService", invoker.LastService);
    }

    [Fact]
    public void CreateProxy_NullInvoker_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => DotBoxdServiceRegistry.CreateProxy(typeof(IGameService), null!));
    }

    [Fact]
    public void CreateProxy_NonInterfaceType_ThrowsArgumentException()
    {
        // Resolve rejects non-interface service types regardless of generated state.
        var ex = Assert.Throws<ArgumentException>(
            () => DotBoxdServiceRegistry.CreateProxy(typeof(TestGameService), new RecordingInvoker()));

        Assert.Contains("must be an interface", ex.Message);
    }

    [Fact]
    public void CreateDispatcher_Typed_ReturnsDispatcherForServiceName()
    {
        var dispatcher = DotBoxdServiceRegistry.CreateDispatcher<IGameService>(new TestGameService());

        Assert.Equal("IGameService", dispatcher.ServiceName);
    }

    [Fact]
    public void CreateDispatcher_ByType_ReturnsDispatcher()
    {
        var dispatcher = DotBoxdServiceRegistry.CreateDispatcher(typeof(IGameService), new TestGameService());

        Assert.IsAssignableFrom<IServiceDispatcher>(dispatcher);
        Assert.Equal("IGameService", dispatcher.ServiceName);
    }

    [Fact]
    public void CreateDispatcher_NullImplementation_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => DotBoxdServiceRegistry.CreateDispatcher(typeof(IGameService), null!));
    }

    [Fact]
    public void CreateDispatcher_ImplementationNotAssignable_ThrowsArgumentException()
    {
        // The implementation does not implement the requested interface, so dispatcher creation must
        // reject it with a descriptive ArgumentException.
        var ex = Assert.Throws<ArgumentException>(
            () => DotBoxdServiceRegistry.CreateDispatcher(typeof(IGameService), new object()));

        Assert.Contains("does not implement", ex.Message);
    }

    [Fact]
    public void RegisterServices_FromGeneratedAssembly_PublishesToSink()
    {
        var sink = new RecordingServiceSink();

        DotBoxdServiceRegistry.RegisterServices(new[] { SharedAssembly }, sink);

        Assert.Contains(typeof(IGameService), sink.ServiceTypes);
        Assert.Contains(typeof(IPlayerNotifications), sink.ServiceTypes);
    }

    [Fact]
    public void RegisterServices_NullAssemblies_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => DotBoxdServiceRegistry.RegisterServices(null!, new RecordingServiceSink()));
    }

    [Fact]
    public void RegisterServices_NullSink_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => DotBoxdServiceRegistry.RegisterServices(new[] { SharedAssembly }, (IDotBoxdServiceRegistrationSink)null!));
    }

    [Fact]
    public void RegisterGeneratedServices_FromGeneratedAssembly_PublishesProxyAndDispatcher()
    {
        var sink = new RecordingGeneratedSink();

        DotBoxdServiceRegistry.RegisterGeneratedServices(new[] { SharedAssembly }, sink);

        Assert.Contains(typeof(IGameService), sink.ServiceTypes);
        Assert.Contains(typeof(IPlayerNotifications), sink.ServiceTypes);
    }

    [Fact]
    public void RegisterGeneratedServices_NullAssemblies_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => DotBoxdServiceRegistry.RegisterGeneratedServices(null!, new RecordingGeneratedSink()));
    }

    [Fact]
    public void RegisterGeneratedServices_NullSink_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => DotBoxdServiceRegistry.RegisterGeneratedServices(
                new[] { SharedAssembly },
                (IDotBoxdGeneratedServiceRegistrationSink)null!));
    }

    [Fact]
    public void RegisterServices_NullAssemblyForCatalog_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => DotBoxdServiceRegistry.RegisterServices(
                (Assembly)null!,
                DotBoxdServiceRegistry.GetServices(SharedAssembly)));
    }

    [Fact]
    public void RegisterServices_NullServicesForCatalog_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => DotBoxdServiceRegistry.RegisterServices(SharedAssembly, null!));
    }

    [Fact]
    public void Register_NullProxyFactory_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            DotBoxdServiceRegistry.Register<ICustomRegisteredService>(
                null!,
                _ => new CustomDispatcher(),
                ValidCustomService()));
    }

    [Fact]
    public void Register_NullDispatcherFactory_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            DotBoxdServiceRegistry.Register<ICustomRegisteredService>(
                _ => new CustomProxy(),
                null!,
                ValidCustomService()));
    }

    [Fact]
    public void Register_MetadataDescribingWrongServiceType_ThrowsArgumentException()
    {
        // ValidateService must reject metadata whose ServiceType disagrees with TService.
        var mismatched = new DotBoxdGeneratedService(
            typeof(IGameService),
            typeof(CustomProxy),
            typeof(CustomDispatcher),
            "Custom");

        var ex = Assert.Throws<ArgumentException>(() =>
            DotBoxdServiceRegistry.Register<ICustomRegisteredService>(
                _ => new CustomProxy(),
                _ => new CustomDispatcher(),
                mismatched));

        Assert.Contains("registered for", ex.Message);
    }

    [Fact]
    public void Register_MetadataMissingServiceName_ThrowsArgumentException()
    {
        var noName = new DotBoxdGeneratedService(
            typeof(ICustomRegisteredService),
            typeof(CustomProxy),
            typeof(CustomDispatcher),
            string.Empty);

        var ex = Assert.Throws<ArgumentException>(() =>
            DotBoxdServiceRegistry.Register<ICustomRegisteredService>(
                _ => new CustomProxy(),
                _ => new CustomDispatcher(),
                noName));

        Assert.Contains("service name", ex.Message);
    }

    [Fact]
    public void Register_WithValidMetadata_MakesServiceResolvable()
    {
        // A full round-trip: register a hand-authored service interface, then resolve its proxy,
        // dispatcher, and metadata through the public API.
        DotBoxdServiceRegistry.Register<ICustomRegisteredService>(
            _ => new CustomProxy(),
            _ => new CustomDispatcher(),
            ValidCustomService());

        var metadata = DotBoxdServiceRegistry.GetService<ICustomRegisteredService>();
        var proxy = DotBoxdServiceRegistry.CreateProxy<ICustomRegisteredService>(new RecordingInvoker());
        var dispatcher = DotBoxdServiceRegistry.CreateDispatcher<ICustomRegisteredService>(new CustomImplementation());

        Assert.Equal("Custom", metadata.ServiceName);
        Assert.IsType<CustomProxy>(proxy);
        Assert.Equal("Custom", dispatcher.ServiceName);
    }

    [Fact]
    public void Register_DuplicateRegistration_ReplacesPreviousFactory()
    {
        DotBoxdServiceRegistry.Register<IReplaceableService>(
            _ => new ReplaceableProxyV1(),
            _ => new CustomDispatcher(),
            new DotBoxdGeneratedService(
                typeof(IReplaceableService),
                typeof(ReplaceableProxyV1),
                typeof(CustomDispatcher),
                "Replaceable"));

        DotBoxdServiceRegistry.Register<IReplaceableService>(
            _ => new ReplaceableProxyV2(),
            _ => new CustomDispatcher(),
            new DotBoxdGeneratedService(
                typeof(IReplaceableService),
                typeof(ReplaceableProxyV2),
                typeof(CustomDispatcher),
                "Replaceable"));

        var proxy = DotBoxdServiceRegistry.CreateProxy<IReplaceableService>(new RecordingInvoker());

        // The latest registration wins.
        Assert.IsType<ReplaceableProxyV2>(proxy);
    }

    private static DotBoxdGeneratedService ValidCustomService() =>
        new(
            typeof(ICustomRegisteredService),
            typeof(CustomProxy),
            typeof(CustomDispatcher),
            "Custom");

    // --- Test-local service surfaces (no generator runs for these) ---

    private interface IUngeneratedCoverageService
    {
        Task PingAsync(CancellationToken ct = default);
    }

    public interface ICustomRegisteredService
    {
        Task DoAsync(CancellationToken ct = default);
    }

    public interface IReplaceableService
    {
        Task DoAsync(CancellationToken ct = default);
    }

    private sealed class CustomImplementation : ICustomRegisteredService
    {
        public Task DoAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class CustomProxy : ICustomRegisteredService
    {
        public Task DoAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class ReplaceableProxyV1 : IReplaceableService
    {
        public Task DoAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class ReplaceableProxyV2 : IReplaceableService
    {
        public Task DoAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class CustomDispatcher : IServiceDispatcher
    {
        public string ServiceName => "Custom";

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class RecordingServiceSink : IDotBoxdServiceRegistrationSink
    {
        public List<Type> ServiceTypes { get; } = new();

        public void AddService<TService, TImplementation>()
            where TService : class
            where TImplementation : TService =>
            ServiceTypes.Add(typeof(TService));
    }

    private sealed class RecordingGeneratedSink : IDotBoxdGeneratedServiceRegistrationSink
    {
        public List<Type> ServiceTypes { get; } = new();

        public void AddService<TService, TProxy, TDispatcher>()
            where TService : class
            where TProxy : TService
            where TDispatcher : IServiceDispatcher =>
            ServiceTypes.Add(typeof(TService));
    }

    /// <summary>
    /// Minimal <see cref="IRpcInvoker"/> that lets a generated proxy run without a transport. Every
    /// no-request/response call returns a canned <see cref="ServerStatus"/> so proxy routing is
    /// observable, and records the last service name forwarded by the proxy.
    /// </summary>
    private sealed class RecordingInvoker : IRpcInvoker
    {
        public string? LastService { get; private set; }

        public Task<TResponse> InvokeAsync<TRequest, TResponse>(
            string service, string method, TRequest request, CancellationToken ct = default)
        {
            LastService = service;
            return Task.FromResult(CannedResponse<TResponse>());
        }

        public Task<TResponse> InvokeAsync<TResponse>(string service, string method, CancellationToken ct = default)
        {
            LastService = service;
            return Task.FromResult(CannedResponse<TResponse>());
        }

        public Task InvokeAsync<TRequest>(string service, string method, TRequest request, CancellationToken ct = default)
        {
            LastService = service;
            return Task.CompletedTask;
        }

        public Task InvokeAsync(string service, string method, CancellationToken ct = default)
        {
            LastService = service;
            return Task.CompletedTask;
        }

        public Task<TResponse> InvokeOnInstanceAsync<TRequest, TResponse>(
            string service, string instanceId, string method, TRequest request, CancellationToken ct = default)
        {
            LastService = service;
            return Task.FromResult(CannedResponse<TResponse>());
        }

        public Task<TResponse> InvokeOnInstanceAsync<TResponse>(
            string service, string instanceId, string method, CancellationToken ct = default)
        {
            LastService = service;
            return Task.FromResult(CannedResponse<TResponse>());
        }

        public Task InvokeOnInstanceAsync<TRequest>(
            string service, string instanceId, string method, TRequest request, CancellationToken ct = default)
        {
            LastService = service;
            return Task.CompletedTask;
        }

        public Task InvokeOnInstanceAsync(
            string service, string instanceId, string method, CancellationToken ct = default)
        {
            LastService = service;
            return Task.CompletedTask;
        }

        private static TResponse CannedResponse<TResponse>()
        {
            if (typeof(TResponse) == typeof(ServerStatus))
            {
                return (TResponse)(object)new ServerStatus
                {
                    PlayerCount = 0,
                    ServerTime = "now",
                    Version = "from-invoker",
                };
            }

            return default!;
        }
    }
}
