using FluentAssertions;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public sealed class StreamingGeneratorDiagnosticTests
{
    [Fact]
    public void NullableStreamingShapes_ProduceUnsupportedDiagnostics()
    {
        const string source = """
            #nullable enable
            using DotBoxD.Services.Attributes;
            using System.Collections.Generic;
            using System.IO;
            using System.IO.Pipelines;
            using System.Threading.Tasks;

            namespace Streaming.Nullable
            {
                [RpcService]
                public interface INullableStreaming
                {
                    Stream? Download();
                    Task<Stream?> DownloadAsync();
                    IAsyncEnumerable<int>? Numbers();
                    Task<IAsyncEnumerable<int>?> NumbersAsync();
                    IAsyncEnumerable<string?> NullableItems();
                    Task<IAsyncEnumerable<string?>> NullableItemsAsync();
                    Task<int> UploadStreamAsync(Stream? bytes);
                    Task<int> UploadPipeAsync(Pipe? pipe);
                    Task<int> UploadItemsAsync(IAsyncEnumerable<int>? items);
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var diagnostics = driver.GetRunResult().Diagnostics
            .Where(d => d.Id == "DBXS002")
            .Select(d => d.GetMessage())
            .ToArray();

        diagnostics.Should().HaveCount(7);
        diagnostics.Should().Contain(m => m.Contains("Download") &&
            m.Contains("nullable streaming return values are not supported"));
        diagnostics.Should().Contain(m => m.Contains("DownloadAsync") &&
            m.Contains("nullable streaming return values are not supported"));
        diagnostics.Should().Contain(m => m.Contains("Numbers") &&
            m.Contains("nullable streaming return values are not supported"));
        diagnostics.Should().Contain(m => m.Contains("NumbersAsync") &&
            m.Contains("nullable streaming return values are not supported"));
        diagnostics.Should().Contain(m => m.Contains("nullable streamed parameter 'bytes'"));
        diagnostics.Should().Contain(m => m.Contains("nullable streamed parameter 'pipe'"));
        diagnostics.Should().Contain(m => m.Contains("nullable streamed parameter 'items'"));
    }

    [Fact]
    public void ConcreteStreamingCompatibleShapes_ProduceUnsupportedDiagnostics()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Collections.Generic;
            using System.IO;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Streaming.Concrete
            {
                public sealed class CustomNumbers : IAsyncEnumerable<int>
                {
                    public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default)
                        => throw new System.NotSupportedException();
                }

                [RpcService]
                public interface IConcreteStreaming
                {
                    MemoryStream Download();
                    Task<MemoryStream> DownloadAsync();
                    CustomNumbers Numbers();
                    Task<int> UploadStreamAsync(MemoryStream bytes);
                    Task<int> UploadItemsAsync(CustomNumbers items);
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var diagnostics = driver.GetRunResult().Diagnostics
            .Where(d => d.Id == "DBXS002")
            .Select(d => d.GetMessage())
            .ToArray();

        diagnostics.Should().HaveCount(5);
        diagnostics.Should().Contain(m => m.Contains("Download") && m.Contains("use System.IO.Stream"));
        diagnostics.Should().Contain(m => m.Contains("DownloadAsync") && m.Contains("use System.IO.Stream"));
        diagnostics.Should().Contain(m => m.Contains("Numbers") && m.Contains("use IAsyncEnumerable<T>"));
        diagnostics.Should().Contain(m => m.Contains("parameter 'bytes'") && m.Contains("use System.IO.Stream"));
        diagnostics.Should().Contain(m => m.Contains("parameter 'items'") && m.Contains("use IAsyncEnumerable<T>"));
    }
}
