using DotBoxD.Services.Generated;
using static DotBoxD.Services.SourceGenerator.Tests.Generation.GeneratedFactoryRegistryTestSupport;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public sealed class GeneratedFactoryDefaultValueMetadataRegressionTests
{
    [Fact]
    public void GeneratedFactory_BoxesValueTypeDefaultParameterMetadata()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Metadata.DefaultBoxing
            {
                [RpcService]
                public interface ITemporalDefaults
                {
                    Task PingAsync(DateTime when = default, Guid id = default, CancellationToken ct = default);
                }
            }
            """;

        var assembly = CompileAndLoad(source);
        var generated = assembly.GetType("DotBoxD.Services.Generated.DotBoxDGenerated")
            ?? throw new InvalidOperationException("Generated factory type not found.");
        var services = Assert.IsAssignableFrom<IReadOnlyList<GeneratedService>>(
            generated.GetProperty("Services")!.GetValue(null));

        var method = Assert.Single(Assert.Single(services).Methods);

        var when = Assert.Single(method.Parameters, parameter => parameter.Name == "when");
        Assert.True(when.HasDefaultValue);
        Assert.Equal(typeof(DateTime), when.Type);
        Assert.Equal(default(DateTime), Assert.IsType<DateTime>(when.DefaultValue));

        var id = Assert.Single(method.Parameters, parameter => parameter.Name == "id");
        Assert.True(id.HasDefaultValue);
        Assert.Equal(typeof(Guid), id.Type);
        Assert.Equal(Guid.Empty, Assert.IsType<Guid>(id.DefaultValue));

        var ct = Assert.Single(method.Parameters, parameter => parameter.Name == "ct");
        Assert.True(ct.HasDefaultValue);
        Assert.Null(ct.DefaultValue);
    }
}
