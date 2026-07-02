using System.Buffers;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Core;

/// <summary>
/// Round-2 coverage for the remaining <see cref="GeneratedServiceRegistry"/> guard/metadata branches the
/// round-1 registry suite did not reach: the two-argument <c>Register</c> overload (default metadata
/// construction), the <c>Resolve</c> null-type guard, the non-interface guard via a framework type,
/// and the <c>ValidateService</c> missing-proxy/dispatcher/name validations. All driven through the
/// public static registry API; the throwaway service interfaces here have no generator output, so the
/// hand-supplied factories and metadata fully control resolution without touching generated state.
/// </summary>
public sealed class RegistryGuardCoverageTests
{
    // ----- Two-arg Register overload builds default metadata (GeneratedServiceRegistry 21-28) -----

    [Fact]
    public void Register_TwoArgOverload_BuildsDefaultMetadataAndResolves()
    {
        // The metadata-less overload synthesizes GeneratedService(TService, TService,
        // IServiceDispatcher, TService.Name) for us. After registering, the metadata, proxy, and
        // dispatcher must all resolve from the hand-supplied factories.
        GeneratedServiceRegistry.Register<IDefaultMetadataService>(
            _ => new DefaultMetadataProxy(),
            _ => new DefaultMetadataDispatcher());

        var metadata = GeneratedServiceRegistry.GetService<IDefaultMetadataService>();
        var proxy = GeneratedServiceRegistry.CreateProxy<IDefaultMetadataService>(new NoopInvoker());
        var dispatcher = GeneratedServiceRegistry.CreateDispatcher<IDefaultMetadataService>(
            new DefaultMetadataImpl());

        // Default metadata: ServiceType == ProxyType == the interface, name defaults to the type name.
        Assert.Equal(typeof(IDefaultMetadataService), metadata.ServiceType);
        Assert.Equal(typeof(IDefaultMetadataService), metadata.ProxyType);
        Assert.Equal(typeof(IServiceDispatcher), metadata.DispatcherType);
        Assert.Equal(nameof(IDefaultMetadataService), metadata.ServiceName);
        Assert.IsType<DefaultMetadataProxy>(proxy);
        Assert.IsAssignableFrom<IServiceDispatcher>(dispatcher);
    }

    // ----- Resolve null-type guard via CreateProxy (GeneratedServiceRegistry 211-213) -----

    [Fact]
    public void CreateProxy_NullServiceInterface_ThrowsArgumentNull()
    {
        // The invoker is non-null, so the call proceeds into Resolve(null) which rejects the null type.
        var ex = Assert.Throws<ArgumentNullException>(
            () => GeneratedServiceRegistry.CreateProxy((Type)null!, new NoopInvoker()));
        Assert.Equal("serviceInterface", ex.ParamName);
    }

    // ----- Non-interface guard with a framework type (GeneratedServiceRegistry 215-220) -----

    [Fact]
    public void CreateProxy_StringType_ThrowsArgumentExceptionMustBeInterface()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => GeneratedServiceRegistry.CreateProxy(typeof(string), new NoopInvoker()));
        Assert.Contains("must be an interface", ex.Message);
        Assert.Equal("serviceInterface", ex.ParamName);
    }

    // ----- ValidateService missing ProxyType (GeneratedServiceRegistry 251-252) -----

    [Fact]
    public void Register_MetadataMissingProxyType_ThrowsArgumentException()
    {
        var noProxy = new GeneratedService(
            typeof(IValidationProbeService),
            ProxyType: null!,
            typeof(ProbeDispatcher),
            "Probe");

        var ex = Assert.Throws<ArgumentException>(() =>
            GeneratedServiceRegistry.Register<IValidationProbeService>(
                _ => new ProbeProxy(),
                _ => new ProbeDispatcher(),
                noProxy));

        Assert.Contains("proxy type", ex.Message);
        Assert.Equal("service", ex.ParamName);
    }

    // ----- ValidateService missing DispatcherType (GeneratedServiceRegistry 255-256) -----

    [Fact]
    public void Register_MetadataMissingDispatcherType_ThrowsArgumentException()
    {
        var noDispatcher = new GeneratedService(
            typeof(IValidationProbeService),
            typeof(ProbeProxy),
            DispatcherType: null!,
            "Probe");

        var ex = Assert.Throws<ArgumentException>(() =>
            GeneratedServiceRegistry.Register<IValidationProbeService>(
                _ => new ProbeProxy(),
                _ => new ProbeDispatcher(),
                noDispatcher));

        Assert.Contains("dispatcher type", ex.Message);
        Assert.Equal("service", ex.ParamName);
    }

    // ----- ValidateService missing ServiceType (GeneratedServiceRegistry 247-248) -----

    [Fact]
    public void Register_MetadataMissingServiceType_ThrowsArgumentException()
    {
        var noServiceType = new GeneratedService(
            ServiceType: null!,
            typeof(ProbeProxy),
            typeof(ProbeDispatcher),
            "Probe");

        var ex = Assert.Throws<ArgumentException>(() =>
            GeneratedServiceRegistry.Register<IValidationProbeService>(
                _ => new ProbeProxy(),
                _ => new ProbeDispatcher(),
                noServiceType));

        Assert.Contains("service type", ex.Message);
        Assert.Equal("service", ex.ParamName);
    }

    // ----- Factory return value guards -----

    [Fact]
    public void CreateProxy_FactoryReturnsNull_ThrowsInvalidOperationException()
    {
        GeneratedServiceRegistry.Register<INullProxyFactoryService>(
            _ => null!,
            _ => new NullProxyFactoryDispatcher());

        var ex = Assert.Throws<InvalidOperationException>(
            () => GeneratedServiceRegistry.CreateProxy<INullProxyFactoryService>(new NoopInvoker()));

        Assert.Contains("proxy", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(INullProxyFactoryService), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateDispatcher_FactoryReturnsNull_ThrowsInvalidOperationException()
    {
        GeneratedServiceRegistry.Register<INullDispatcherFactoryService>(
            _ => new NullDispatcherFactoryProxy(),
            _ => null!);

        var ex = Assert.Throws<InvalidOperationException>(
            () => GeneratedServiceRegistry.CreateDispatcher<INullDispatcherFactoryService>(
                new NullDispatcherFactoryImpl()));

        Assert.Contains("dispatcher", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(INullDispatcherFactoryService), ex.Message, StringComparison.Ordinal);
    }

    // --- Throwaway service surfaces (no generator runs for these) ---

    public interface IDefaultMetadataService
    {
        Task DoAsync(CancellationToken ct = default);
    }

    public interface IValidationProbeService
    {
        Task DoAsync(CancellationToken ct = default);
    }

    public interface INullProxyFactoryService
    {
        Task DoAsync(CancellationToken ct = default);
    }

    public interface INullDispatcherFactoryService
    {
        Task DoAsync(CancellationToken ct = default);
    }

    private sealed class DefaultMetadataImpl : IDefaultMetadataService
    {
        public Task DoAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class DefaultMetadataProxy : IDefaultMetadataService
    {
        public Task DoAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class ProbeProxy : IValidationProbeService
    {
        public Task DoAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullDispatcherFactoryImpl : INullDispatcherFactoryService
    {
        public Task DoAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullDispatcherFactoryProxy : INullDispatcherFactoryService
    {
        public Task DoAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class DefaultMetadataDispatcher : IServiceDispatcher
    {
        public string ServiceName => nameof(IDefaultMetadataService);

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullProxyFactoryDispatcher : IServiceDispatcher
    {
        public string ServiceName => nameof(INullProxyFactoryService);

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class ProbeDispatcher : IServiceDispatcher
    {
        public string ServiceName => "Probe";

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>An <see cref="IRpcInvoker"/> that is never actually invoked by these tests.</summary>
    private sealed class NoopInvoker : IRpcInvoker
    {
        public Task<TResponse> InvokeAsync<TRequest, TResponse>(
            string service, string method, TRequest request, CancellationToken ct = default) =>
            Task.FromResult(default(TResponse)!);

        public Task<TResponse> InvokeAsync<TResponse>(string service, string method, CancellationToken ct = default) =>
            Task.FromResult(default(TResponse)!);

        public Task InvokeAsync<TRequest>(string service, string method, TRequest request, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task InvokeAsync(string service, string method, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<TResponse> InvokeOnInstanceAsync<TRequest, TResponse>(
            string service, string instanceId, string method, TRequest request, CancellationToken ct = default) =>
            Task.FromResult(default(TResponse)!);

        public Task<TResponse> InvokeOnInstanceAsync<TResponse>(
            string service, string instanceId, string method, CancellationToken ct = default) =>
            Task.FromResult(default(TResponse)!);

        public Task InvokeOnInstanceAsync<TRequest>(
            string service, string instanceId, string method, TRequest request, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task InvokeOnInstanceAsync(
            string service, string instanceId, string method, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
