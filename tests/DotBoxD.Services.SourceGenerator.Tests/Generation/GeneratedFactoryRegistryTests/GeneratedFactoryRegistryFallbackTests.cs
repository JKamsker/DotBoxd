using DotBoxD.Services.Generated;
using static DotBoxD.Services.SourceGenerator.Tests.Generation.GeneratedFactoryRegistryTestSupport;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public class GeneratedFactoryRegistryFallbackTests
{
    [Fact]
    public void Registry_ReportsClearDiagnosticWhenGeneratorDidNotRun()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            GeneratedServiceRegistry.CreateProxy(typeof(INotGeneratedService), new NullClient()));

        Assert.Contains("No DotBoxD generated factory is registered", ex.Message);
        Assert.Contains("[DotBoxDService]", ex.Message);
        Assert.Contains("source generator", ex.Message);
    }

    [Fact]
    public void Registry_ReturnsEmptyServiceCatalogWhenAssemblyHasNoGeneratedRegistry()
    {
        var assembly = typeof(GeneratedFactoryRegistryTests).Assembly;

        var services = GeneratedServiceRegistry.GetServices(assembly);

        Assert.Empty(services);
        Assert.Same(services, GeneratedServiceRegistry.GetServices(assembly));
    }

    [Fact]
    public void Registry_ReadsLegacyGeneratedServicesCatalog()
    {
        const string source = """
            using System.Collections.Generic;
            using DotBoxD.Services.Generated;

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

            namespace DotBoxD.Services.Generated
            {
                public static class DotBoxDGenerated
                {
                    private static readonly GeneratedService[] s_services =
                    {
                        new GeneratedService(
                            typeof(global::Legacy.Sample.ILegacyService),
                            typeof(global::Legacy.Sample.LegacyServiceProxy),
                            typeof(global::Legacy.Sample.LegacyServiceDispatcher),
                            "ILegacyService"),
                    };

                    public static IReadOnlyList<GeneratedService> Services => s_services;
                }
            }
            """;

        var assembly = CompileAndLoad(source);

        var services = GeneratedServiceRegistry.GetServices(assembly);

        var service = Assert.Single(services);
        Assert.Equal("ILegacyService", service.ServiceName);
        Assert.Equal("LegacyServiceProxy", service.ProxyType.Name);
        Assert.Same(services, GeneratedServiceRegistry.GetServices(assembly));
    }
}
