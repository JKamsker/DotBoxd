using Microsoft.CodeAnalysis;
using static DotBoxD.Services.SourceGenerator.Tests.Generation.GeneratedFactoryRegistryTestSupport;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public class GeneratedDispatcherSurfaceTests
{
    [Fact]
    public void NestedServiceInstanceDispatcher_PublicSurfaceCompilesFromConsumerAssembly()
    {
        var generatedAssembly = CompileGeneratedReference("""
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace NestedSurface.Sample
            {
                [RpcService]
                public interface IChild
                {
                    Task<int> CountAsync();
                }

                [RpcService]
                public interface IRoot
                {
                    Task<IChild> OpenAsync();
                }
            }
            """);
        var consumer = GeneratorTestHelper.CreateCompilation("""
            using DotBoxD.Services.Peer;
            using DotBoxD.Services.Server;
            using NestedSurface.Sample;

            namespace NestedSurface.Consumer
            {
                public static class Probe
                {
                    public static RpcPeer ProvideChildDispatcher(RpcPeer peer)
                    {
                        return peer.Provide((IServiceDispatcher)new ChildDispatcher());
                    }
                }
            }
            """).AddReferences(generatedAssembly);

        using var stream = new MemoryStream();
        var emit = consumer.Emit(stream);

        Assert.True(
            emit.Success,
            string.Join(Environment.NewLine, emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
    }
}
