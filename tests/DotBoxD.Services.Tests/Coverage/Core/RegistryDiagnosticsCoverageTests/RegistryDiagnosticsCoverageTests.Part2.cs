using System.Buffers;
using System.Reflection;
using System.Reflection.Emit;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Core;

public sealed partial class GeneratedServiceRegistryCoverageTests
{
    [Fact]
    public void Register_MetadataProxyTypeNotAssignableToService_ThrowsArgumentException()
    {
        var service = ValidInvalidMetadataService() with { ProxyType = typeof(object) };

        var ex = Assert.Throws<ArgumentException>(() =>
            GeneratedServiceRegistry.Register<IInvalidMetadataService>(
                _ => new InvalidMetadataProxy(),
                _ => new InvalidMetadataDispatcher(),
                service));

        Assert.Equal("service", ex.ParamName);
        Assert.Contains("proxy", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Register_MetadataDispatcherTypeNotDispatcher_ThrowsArgumentException()
    {
        var service = ValidInvalidMetadataService() with { DispatcherType = typeof(object) };

        var ex = Assert.Throws<ArgumentException>(() =>
            GeneratedServiceRegistry.Register<IInvalidMetadataService>(
                _ => new InvalidMetadataProxy(),
                _ => new InvalidMetadataDispatcher(),
                service));

        Assert.Equal("service", ex.ParamName);
        Assert.Contains("dispatcher", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(InvalidCatalogServices))]
    public void RegisterServices_RejectsMalformedMethodMetadataBeforePublishing(
        string scenario,
        GeneratedService service)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("DotBoxD.Services.Tests.InvalidCatalog." + scenario + "." + Guid.NewGuid().ToString("N")),
            AssemblyBuilderAccess.Run);

        var ex = Assert.Throws<ArgumentException>(() =>
            GeneratedServiceRegistry.RegisterServices(assembly, new[] { service }));

        Assert.Equal("services", ex.ParamName);
        Assert.Empty(GeneratedServiceRegistry.GetServices(assembly));
    }

    public static TheoryData<string, GeneratedService> InvalidCatalogServices => new()
    {
        { "NullMethodName", ServiceWithMethod(ValidMethod() with { Name = null! }) },
        { "EmptyMethodName", ServiceWithMethod(ValidMethod() with { Name = string.Empty }) },
        { "NullWireName", ServiceWithMethod(ValidMethod() with { WireName = null! }) },
        { "EmptyWireName", ServiceWithMethod(ValidMethod() with { WireName = string.Empty }) },
        { "NullReturnType", ServiceWithMethod(ValidMethod() with { ReturnType = null! }) },
        { "UndefinedReturnKind", ServiceWithMethod(ValidMethod() with { ReturnKind = (GeneratedReturnKind)999 }) },
        {
            "NullParameterType",
            ServiceWithMethod(ValidMethod() with { Parameters = new[] { ValidParameter() with { Type = null! } } })
        },
        {
            "NegativeParameterPosition",
            ServiceWithMethod(ValidMethod() with { Parameters = new[] { ValidParameter() with { Position = -1 } } })
        },
        {
            "OutOfOrderParameterPositions",
            ServiceWithMethod(ValidMethod() with
            {
                Parameters = new[]
                {
                    ValidParameter() with { Name = "second", Position = 1 },
                    ValidParameter() with { Name = "first", Position = 0 }
                }
            })
        }
    };

    private static GeneratedService ValidInvalidMetadataService() =>
        new(
            typeof(IInvalidMetadataService),
            typeof(InvalidMetadataProxy),
            typeof(InvalidMetadataDispatcher),
            "InvalidMetadata");

    private static GeneratedService ServiceWithMethod(GeneratedMethod method) =>
        ValidInvalidMetadataService() with { Methods = new[] { method } };

    private static GeneratedMethod ValidMethod() =>
        new(
            "DoAsync",
            "do",
            typeof(Task),
            ResultType: null,
            GeneratedReturnKind.Task,
            ReturnsNestedService: false,
            new[] { ValidParameter() });

    private static GeneratedParameter ValidParameter() =>
        new(
            "ct",
            typeof(CancellationToken),
            Position: 0,
            IsCancellationToken: true,
            HasDefaultValue: true,
            DefaultValue: null);

    public interface IInvalidMetadataService
    {
        Task DoAsync(CancellationToken ct = default);
    }

    private sealed class InvalidMetadataProxy : IInvalidMetadataService
    {
        public Task DoAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class InvalidMetadataDispatcher : IServiceDispatcher
    {
        public string ServiceName => "InvalidMetadata";

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default) => Task.CompletedTask;
    }
}
