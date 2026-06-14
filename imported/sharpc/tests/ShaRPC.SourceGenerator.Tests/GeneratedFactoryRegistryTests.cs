using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ShaRPC.Core.Client;
using ShaRPC.Core.Generated;
using ShaRPC.Core.Server;
using Xunit;

namespace ShaRPC.SourceGenerator.Tests;

public class GeneratedFactoryRegistryTests
{
    [Fact]
    public void GeneratedFactory_CreatesProxyAndDispatcherWithoutScanning()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Factory.Sample
            {
                [ShaRpcService]
                public interface IGreeter
                {
                    Task<string> HelloAsync();
                }

                public sealed class Greeter : IGreeter
                {
                    public Task<string> HelloAsync() => Task.FromResult("hello");
                }
            }
            """;

        var assembly = CompileAndLoad(source);
        var interfaceType = assembly.GetType("Factory.Sample.IGreeter")!;
        var implementation = Activator.CreateInstance(assembly.GetType("Factory.Sample.Greeter")!)!;
        var client = new NullClient();

        var generated = assembly.GetType("ShaRPC.Generated.ShaRpcGenerated")
            ?? throw new InvalidOperationException("Generated factory type not found.");
        var proxy = generated
            .GetMethod("CreateProxy", new[] { typeof(Type), typeof(global::ShaRPC.Core.IRpcInvoker) })!
            .Invoke(null, new object[] { interfaceType, client });
        var dispatcher = generated
            .GetMethod("CreateDispatcher", new[] { typeof(Type), typeof(object) })!
            .Invoke(null, new[] { interfaceType, implementation });
        var services = Assert.IsAssignableFrom<IReadOnlyList<ShaRpcGeneratedService>>(
            generated.GetProperty("Services")!.GetValue(null));

        Assert.True(interfaceType.IsInstanceOfType(proxy));
        Assert.IsAssignableFrom<IServiceDispatcher>(dispatcher);
        Assert.Single(services);
        Assert.Equal(interfaceType, services[0].ServiceType);
        Assert.Equal("IGreeter", services[0].ServiceName);
        Assert.Equal("GreeterProxy", services[0].ProxyType.Name);
        Assert.Equal("GreeterDispatcher", services[0].DispatcherType.Name);

        var sink = new RegistrationSink();
        generated.GetMethod("RegisterServices")!.Invoke(null, new object[] { sink });
        var sinkService = Assert.Single(sink.Services);

        Assert.Equal(interfaceType, sinkService.ServiceType);
        Assert.Equal("GreeterProxy", sinkService.ImplementationType.Name);

        var generatedSink = new GeneratedRegistrationSink();
        generated.GetMethod("RegisterGeneratedServices")!.Invoke(null, new object[] { generatedSink });
        var generatedSinkService = Assert.Single(generatedSink.Services);

        Assert.Equal(interfaceType, generatedSinkService.ServiceType);
        Assert.Equal("GreeterProxy", generatedSinkService.ProxyType.Name);
        Assert.Equal("GreeterDispatcher", generatedSinkService.DispatcherType.Name);

        var assemblyServices = ShaRpcServiceRegistry.GetServices(assembly);
        Assert.Same(services, assemblyServices);

        var combinedServices = ShaRpcServiceRegistry.GetServices(new[] { assembly, typeof(NullClient).Assembly });
        Assert.Contains(combinedServices, service => service.ServiceType == interfaceType);

        var multiSink = new RegistrationSink();
        ShaRpcServiceRegistry.RegisterServices(new[] { assembly }, multiSink);
        Assert.Equal(interfaceType, Assert.Single(multiSink.Services).ServiceType);

        var multiGeneratedSink = new GeneratedRegistrationSink();
        ShaRpcServiceRegistry.RegisterGeneratedServices(new[] { assembly }, multiGeneratedSink);
        Assert.Equal(interfaceType, Assert.Single(multiGeneratedSink.Services).ServiceType);

        var registryProxy = ShaRpcServiceRegistry.CreateProxy(interfaceType, client);
        var registryDispatcher = ShaRpcServiceRegistry.CreateDispatcher(interfaceType, implementation);
        var registryService = ShaRpcServiceRegistry.GetService(interfaceType);

        Assert.True(interfaceType.IsInstanceOfType(registryProxy));
        Assert.Equal("IGreeter", registryDispatcher.ServiceName);
        Assert.Equal(services[0], registryService);
    }

    [Fact]
    public void GeneratedFactory_ExposesServiceMethodMetadata()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Metadata.Sample
            {
                [ShaRpcService(Name = "ChildWire")]
                public interface IChild
                {
                    ValueTask<int> CountAsync(CancellationToken ct = default);
                }

                [ShaRpcService(Name = "RootWire")]
                public interface IRoot
                {
                    [ShaRpcMethod(Name = "sum")]
                    Task<int> AddAsync(int a, string label = "guest", CancellationToken ct = default);

                    ValueTask<string> NameAsync(int id = 7);

                    Task<IChild> OpenAsync();

                    int Sync(int value);

                    void Ping();
                }
            }
            """;

        var assembly = CompileAndLoad(source);
        var rootType = assembly.GetType("Metadata.Sample.IRoot")!;
        var childType = assembly.GetType("Metadata.Sample.IChild")!;
        var generated = assembly.GetType("ShaRPC.Generated.ShaRpcGenerated")
            ?? throw new InvalidOperationException("Generated factory type not found.");
        var services = Assert.IsAssignableFrom<IReadOnlyList<ShaRpcGeneratedService>>(
            generated.GetProperty("Services")!.GetValue(null));

        var root = services.Single(service => service.ServiceType == rootType);
        var child = services.Single(service => service.ServiceType == childType);

        Assert.Equal("RootWire", root.ServiceName);
        Assert.Equal("ChildWire", child.ServiceName);
        Assert.Equal(5, root.Methods.Count);

        var add = root.Methods.Single(method => method.Name == "AddAsync");
        Assert.Equal("sum", add.WireName);
        Assert.Equal(typeof(Task<int>), add.ReturnType);
        Assert.Equal(typeof(int), add.ResultType);
        Assert.Equal(ShaRpcGeneratedReturnKind.TaskOfT, add.ReturnKind);
        Assert.False(add.ReturnsNestedService);

        Assert.Equal(3, add.Parameters.Count);
        Assert.Equal("a", add.Parameters[0].Name);
        Assert.Equal(typeof(int), add.Parameters[0].Type);
        Assert.Equal(0, add.Parameters[0].Position);
        Assert.False(add.Parameters[0].HasDefaultValue);
        Assert.Null(add.Parameters[0].DefaultValue);

        Assert.Equal("label", add.Parameters[1].Name);
        Assert.Equal(typeof(string), add.Parameters[1].Type);
        Assert.Equal(1, add.Parameters[1].Position);
        Assert.True(add.Parameters[1].HasDefaultValue);
        Assert.Equal("guest", add.Parameters[1].DefaultValue);

        Assert.Equal("ct", add.Parameters[2].Name);
        Assert.Equal(typeof(CancellationToken), add.Parameters[2].Type);
        Assert.Equal(2, add.Parameters[2].Position);
        Assert.True(add.Parameters[2].IsCancellationToken);
        Assert.True(add.Parameters[2].HasDefaultValue);
        Assert.Null(add.Parameters[2].DefaultValue);

        var name = root.Methods.Single(method => method.Name == "NameAsync");
        Assert.Equal(typeof(ValueTask<string>), name.ReturnType);
        Assert.Equal(typeof(string), name.ResultType);
        Assert.Equal(ShaRpcGeneratedReturnKind.ValueTaskOfT, name.ReturnKind);
        Assert.Equal(7, name.Parameters.Single().DefaultValue);

        var open = root.Methods.Single(method => method.Name == "OpenAsync");
        Assert.Equal(typeof(Task<>).MakeGenericType(childType), open.ReturnType);
        Assert.Equal(childType, open.ResultType);
        Assert.Equal(ShaRpcGeneratedReturnKind.TaskOfNestedService, open.ReturnKind);
        Assert.True(open.ReturnsNestedService);

        var sync = root.Methods.Single(method => method.Name == "Sync");
        Assert.Equal(typeof(int), sync.ReturnType);
        Assert.Null(sync.ResultType);
        Assert.Equal(ShaRpcGeneratedReturnKind.Sync, sync.ReturnKind);

        var ping = root.Methods.Single(method => method.Name == "Ping");
        Assert.Equal(typeof(void), ping.ReturnType);
        Assert.Null(ping.ResultType);
        Assert.Equal(ShaRpcGeneratedReturnKind.Void, ping.ReturnKind);

        var registryService = ShaRpcServiceRegistry.GetService(rootType);
        Assert.Same(root.Methods, registryService.Methods);
    }

    [Fact]
    public void Registry_ReportsClearDiagnosticWhenGeneratorDidNotRun()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ShaRpcServiceRegistry.CreateProxy(typeof(INotGeneratedService), new NullClient()));

        Assert.Contains("No ShaRPC generated factory is registered", ex.Message);
        Assert.Contains("[ShaRpcService]", ex.Message);
        Assert.Contains("source generator", ex.Message);
    }

    [Fact]
    public void Registry_ReturnsEmptyServiceCatalogWhenAssemblyHasNoGeneratedRegistry()
    {
        var assembly = typeof(GeneratedFactoryRegistryTests).Assembly;

        var services = ShaRpcServiceRegistry.GetServices(assembly);

        Assert.Empty(services);
        Assert.Same(services, ShaRpcServiceRegistry.GetServices(assembly));
    }

    [Fact]
    public void Registry_ReadsLegacyGeneratedServicesCatalog()
    {
        const string source = """
            using System.Collections.Generic;
            using ShaRPC.Core.Generated;

            namespace Legacy.Sample
            {
                public interface ILegacyService
                {
                }

                public sealed class LegacyServiceProxy : ILegacyService
                {
                }

                public sealed class LegacyServiceDispatcher
                {
                }
            }

            namespace ShaRPC.Generated
            {
                public static class ShaRpcGenerated
                {
                    private static readonly ShaRpcGeneratedService[] s_services =
                    {
                        new ShaRpcGeneratedService(
                            typeof(global::Legacy.Sample.ILegacyService),
                            typeof(global::Legacy.Sample.LegacyServiceProxy),
                            typeof(global::Legacy.Sample.LegacyServiceDispatcher),
                            "ILegacyService"),
                    };

                    public static IReadOnlyList<ShaRpcGeneratedService> Services => s_services;
                }
            }
            """;

        var assembly = CompileAndLoad(source);

        var services = ShaRpcServiceRegistry.GetServices(assembly);

        var service = Assert.Single(services);
        Assert.Equal("ILegacyService", service.ServiceName);
        Assert.Equal("LegacyServiceProxy", service.ProxyType.Name);
        Assert.Same(services, ShaRpcServiceRegistry.GetServices(assembly));
    }

    [Fact]
    public void GeneratedMetadata_ServiceNameWithBackslash_IsNotDoubleEscaped()
    {
        // A wire name containing a backslash exercises literal escaping. The model stores the name
        // already-escaped, so the generated registry metadata must not escape it a second time —
        // otherwise ShaRpcGenerated.Services[0].ServiceName would disagree with the dispatcher's
        // ServiceName (which inserts the same stored name directly into a string literal).
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Escape.Sample
            {
                [ShaRpcService(Name = "svc\\path")]
                public interface IThing
                {
                    Task PingAsync();
                }

                public sealed class Thing : IThing
                {
                    public Task PingAsync() => Task.CompletedTask;
                }
            }
            """;

        var assembly = CompileAndLoad(source);
        var interfaceType = assembly.GetType("Escape.Sample.IThing")!;
        var implementation = Activator.CreateInstance(assembly.GetType("Escape.Sample.Thing")!)!;

        var generated = assembly.GetType("ShaRPC.Generated.ShaRpcGenerated")
            ?? throw new InvalidOperationException("Generated factory type not found.");
        var services = Assert.IsAssignableFrom<IReadOnlyList<ShaRpcGeneratedService>>(
            generated.GetProperty("Services")!.GetValue(null));
        var dispatcher = (IServiceDispatcher)generated
            .GetMethod("CreateDispatcher", new[] { typeof(Type), typeof(object) })!
            .Invoke(null, new[] { interfaceType, implementation })!;

        // The true semantic wire name is a single backslash: svc\path. Double-escaping would yield
        // svc\\path in the metadata.
        Assert.Equal("svc\\path", services[0].ServiceName);
        // Metadata and the dispatcher must agree on the wire name.
        Assert.Equal(dispatcher.ServiceName, services[0].ServiceName);
    }

    private interface INotGeneratedService
    {
    }

    private sealed class RegistrationSink : IShaRpcServiceRegistrationSink
    {
        public List<ServiceRegistration> Services { get; } = new();

        public void AddService<TService, TImplementation>()
            where TService : class
            where TImplementation : TService =>
            Services.Add(new ServiceRegistration(typeof(TService), typeof(TImplementation)));
    }

    private readonly record struct ServiceRegistration(Type ServiceType, Type ImplementationType);

    private sealed class GeneratedRegistrationSink : IShaRpcGeneratedServiceRegistrationSink
    {
        public List<GeneratedServiceRegistration> Services { get; } = new();

        public void AddService<TService, TProxy, TDispatcher>()
            where TService : class
            where TProxy : TService
            where TDispatcher : IServiceDispatcher =>
            Services.Add(new GeneratedServiceRegistration(
                typeof(TService),
                typeof(TProxy),
                typeof(TDispatcher)));
    }

    private readonly record struct GeneratedServiceRegistration(
        Type ServiceType,
        Type ProxyType,
        Type DispatcherType);

    private static Assembly CompileAndLoad(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        var errors = runResult.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToArray();
        if (errors.Length > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }

        var finalCompilation = ((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees);
        using var stream = new MemoryStream();
        var emit = finalCompilation.Emit(stream);
        if (!emit.Success)
        {
            throw new InvalidOperationException(string.Join(
                Environment.NewLine,
                emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        }

        stream.Position = 0;
        var context = new AssemblyLoadContext("FactoryTest_" + Guid.NewGuid().ToString("N"), isCollectible: false);
        return context.LoadFromStream(stream);
    }

    private sealed class NullClient : global::ShaRPC.Core.IRpcInvoker
    {
        public bool IsConnected => true;

        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => default;

        public Task<TResponse> InvokeAsync<TRequest, TResponse>(
            string service,
            string method,
            TRequest request,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<TResponse> InvokeAsync<TResponse>(
            string service,
            string method,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task InvokeAsync<TRequest>(
            string service,
            string method,
            TRequest request,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task InvokeAsync(string service, string method, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<TResponse> InvokeOnInstanceAsync<TRequest, TResponse>(
            string service,
            string instanceId,
            string method,
            TRequest request,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<TResponse> InvokeOnInstanceAsync<TResponse>(
            string service,
            string instanceId,
            string method,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task InvokeOnInstanceAsync<TRequest>(
            string service,
            string instanceId,
            string method,
            TRequest request,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task InvokeOnInstanceAsync(
            string service,
            string instanceId,
            string method,
            CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
