using DotBoxD.Services.Generated;
using DotBoxD.Services.Server;
using static DotBoxD.Services.SourceGenerator.Tests.Generation.GeneratedFactoryRegistryTestSupport;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public class GeneratedFactoryRegistryTests
{
    [Fact]
    public void GeneratedFactory_CreatesProxyAndDispatcherWithoutScanning()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Factory.Sample
            {
                [DotBoxDService]
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

        var generated = assembly.GetType("DotBoxD.Services.Generated.DotBoxDGenerated")
            ?? throw new InvalidOperationException("Generated factory type not found.");
        var proxy = generated
            .GetMethod("CreateProxy", new[] { typeof(Type), typeof(global::DotBoxD.Services.Server.IRpcInvoker) })!
            .Invoke(null, new object[] { interfaceType, client });
        var dispatcher = generated
            .GetMethod("CreateDispatcher", new[] { typeof(Type), typeof(object) })!
            .Invoke(null, new[] { interfaceType, implementation });
        var services = Assert.IsAssignableFrom<IReadOnlyList<GeneratedService>>(
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

        var assemblyServices = GeneratedServiceRegistry.GetServices(assembly);
        Assert.Same(services, assemblyServices);

        var combinedServices = GeneratedServiceRegistry.GetServices(new[] { assembly, typeof(NullClient).Assembly });
        Assert.Contains(combinedServices, service => service.ServiceType == interfaceType);

        var multiSink = new RegistrationSink();
        GeneratedServiceRegistry.RegisterServices(new[] { assembly }, multiSink);
        Assert.Equal(interfaceType, Assert.Single(multiSink.Services).ServiceType);

        var multiGeneratedSink = new GeneratedRegistrationSink();
        GeneratedServiceRegistry.RegisterGeneratedServices(new[] { assembly }, multiGeneratedSink);
        Assert.Equal(interfaceType, Assert.Single(multiGeneratedSink.Services).ServiceType);

        var registryProxy = GeneratedServiceRegistry.CreateProxy(interfaceType, client);
        var registryDispatcher = GeneratedServiceRegistry.CreateDispatcher(interfaceType, implementation);
        var registryService = GeneratedServiceRegistry.GetService(interfaceType);

        Assert.True(interfaceType.IsInstanceOfType(registryProxy));
        Assert.Equal("IGreeter", registryDispatcher.ServiceName);
        Assert.Equal(services[0], registryService);
    }

    [Fact]
    public void GeneratedFactory_ExposesServiceMethodMetadata()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Metadata.Sample
            {
                [DotBoxDService(Name = "ChildWire")]
                public interface IChild
                {
                    ValueTask<int> CountAsync(CancellationToken ct = default);
                }

                [DotBoxDService(Name = "RootWire")]
                public interface IRoot
                {
                    [DotBoxDMethod(Name = "sum")]
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
        var generated = assembly.GetType("DotBoxD.Services.Generated.DotBoxDGenerated")
            ?? throw new InvalidOperationException("Generated factory type not found.");
        var services = Assert.IsAssignableFrom<IReadOnlyList<GeneratedService>>(
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
        Assert.Equal(GeneratedReturnKind.TaskOfT, add.ReturnKind);
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
        Assert.Equal(GeneratedReturnKind.ValueTaskOfT, name.ReturnKind);
        Assert.Equal(7, name.Parameters.Single().DefaultValue);

        var open = root.Methods.Single(method => method.Name == "OpenAsync");
        Assert.Equal(typeof(Task<>).MakeGenericType(childType), open.ReturnType);
        Assert.Equal(childType, open.ResultType);
        Assert.Equal(GeneratedReturnKind.TaskOfNestedService, open.ReturnKind);
        Assert.True(open.ReturnsNestedService);

        var sync = root.Methods.Single(method => method.Name == "Sync");
        Assert.Equal(typeof(int), sync.ReturnType);
        Assert.Null(sync.ResultType);
        Assert.Equal(GeneratedReturnKind.Sync, sync.ReturnKind);

        var ping = root.Methods.Single(method => method.Name == "Ping");
        Assert.Equal(typeof(void), ping.ReturnType);
        Assert.Null(ping.ResultType);
        Assert.Equal(GeneratedReturnKind.Void, ping.ReturnKind);

        var registryService = GeneratedServiceRegistry.GetService(rootType);
        Assert.Same(root.Methods, registryService.Methods);
    }

    [Fact]
    public void GeneratedMetadata_ServiceNameWithBackslash_IsNotDoubleEscaped()
    {
        // A wire name containing a backslash exercises literal escaping. The model stores the name
        // already-escaped, so the generated registry metadata must not escape it a second time —
        // otherwise DotBoxDGenerated.Services[0].ServiceName would disagree with the dispatcher's
        // ServiceName (which inserts the same stored name directly into a string literal).
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Escape.Sample
            {
                [DotBoxDService(Name = "svc\\path")]
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

        var generated = assembly.GetType("DotBoxD.Services.Generated.DotBoxDGenerated")
            ?? throw new InvalidOperationException("Generated factory type not found.");
        var services = Assert.IsAssignableFrom<IReadOnlyList<GeneratedService>>(
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

}
