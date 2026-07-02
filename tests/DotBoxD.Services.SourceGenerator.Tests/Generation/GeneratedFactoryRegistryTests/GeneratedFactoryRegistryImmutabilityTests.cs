using DotBoxD.Services.Generated;
using static DotBoxD.Services.SourceGenerator.Tests.Generation.GeneratedFactoryRegistryTestSupport;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public sealed class GeneratedFactoryRegistryImmutabilityTests
{
    [Fact]
    public void GeneratedFactory_ServicesCatalogDoesNotExposeMutableArrays()
    {
        var assembly = CompileAndLoad(CatalogSource);
        var serviceType = assembly.GetType("Metadata.ImmutableCatalog.IImmutableCatalog")!;
        var generated = assembly.GetType("DotBoxD.Services.Generated.DotBoxDGenerated")
            ?? throw new InvalidOperationException("Generated factory type not found.");
        var services = Assert.IsAssignableFrom<IReadOnlyList<GeneratedService>>(
            generated.GetProperty("Services")!.GetValue(null));

        AssertCatalogCannotBeMutatedThroughReadOnlyInterfaces(services, serviceType);
    }

    [Fact]
    public void Registry_ServicesCatalogDoesNotExposeMutableArrays()
    {
        var assembly = CompileAndLoad(CatalogSource);
        var serviceType = assembly.GetType("Metadata.ImmutableCatalog.IImmutableCatalog")!;
        var services = GeneratedServiceRegistry.GetServices(assembly);

        AssertCatalogCannotBeMutatedThroughReadOnlyInterfaces(services, serviceType);
        AssertCatalogCannotBeMutatedThroughReadOnlyInterfaces(
            GeneratedServiceRegistry.GetServices(assembly),
            serviceType);
        AssertCatalogCannotBeMutatedThroughReadOnlyInterfaces(
            GeneratedServiceRegistry.GetServices(new[] { assembly }),
            serviceType);
    }

    [Fact]
    public void Registry_ServiceMetadataDoesNotExposeMutableMethodArrays()
    {
        var assembly = CompileAndLoad(CatalogSource);
        var serviceType = assembly.GetType("Metadata.ImmutableCatalog.IImmutableCatalog")!;
        var service = GeneratedServiceRegistry.GetService(serviceType);

        AssertServiceCannotBeMutatedThroughReadOnlyInterfaces(service, serviceType);
        AssertServiceCannotBeMutatedThroughReadOnlyInterfaces(
            GeneratedServiceRegistry.GetService(serviceType),
            serviceType);
    }

    private static void AssertCatalogCannotBeMutatedThroughReadOnlyInterfaces(
        IReadOnlyList<GeneratedService> services,
        Type serviceType)
    {
        var service = Assert.Single(services);
        Assert.Equal(serviceType, service.ServiceType);

        if (services is GeneratedService[] mutableServices)
        {
            mutableServices[0] = service with { ServiceName = "corrupted" };
        }
        else if (services is IList<GeneratedService> { IsReadOnly: false } mutableServiceList)
        {
            mutableServiceList[0] = service with { ServiceName = "corrupted" };
        }

        Assert.Equal(serviceType, services[0].ServiceType);
        Assert.Equal("IImmutableCatalog", services[0].ServiceName);
        AssertServiceCannotBeMutatedThroughReadOnlyInterfaces(services[0], serviceType);
    }

    private static void AssertServiceCannotBeMutatedThroughReadOnlyInterfaces(
        GeneratedService service,
        Type serviceType)
    {
        Assert.Equal(serviceType, service.ServiceType);

        var methods = service.Methods;
        var method = Assert.Single(methods);
        Assert.Equal("EchoAsync", method.Name);

        if (methods is GeneratedMethod[] mutableMethods)
        {
            mutableMethods[0] = method with { Name = "corrupted" };
        }
        else if (methods is IList<GeneratedMethod> { IsReadOnly: false } mutableMethodList)
        {
            mutableMethodList[0] = method with { Name = "corrupted" };
        }

        Assert.Equal("EchoAsync", methods[0].Name);

        var parameters = methods[0].Parameters;
        var parameter = Assert.Single(parameters);
        Assert.Equal("value", parameter.Name);

        if (parameters is GeneratedParameter[] mutableParameters)
        {
            mutableParameters[0] = parameter with { Name = "corrupted" };
        }
        else if (parameters is IList<GeneratedParameter> { IsReadOnly: false } mutableParameterList)
        {
            mutableParameterList[0] = parameter with { Name = "corrupted" };
        }

        Assert.Equal("value", parameters[0].Name);
    }

    private const string CatalogSource = """
        using DotBoxD.Services.Attributes;
        using System.Threading.Tasks;

        namespace Metadata.ImmutableCatalog
        {
            [RpcService]
            public interface IImmutableCatalog
            {
                Task<int> EchoAsync(int value);
            }
        }
        """;
}
