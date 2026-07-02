using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public sealed class StreamingSingleStreamGeneratorTests
{
    [Fact]
    public void SingleStreamedArgument_EmitsSingleAttachmentInsteadOfArray()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.IO;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Streaming.Single
            {
                [RpcService]
                public interface IUpload
                {
                    Task<int> UploadAsync(Stream bytes, CancellationToken ct = default);
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        runResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
        EmitShouldSucceed(((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees));

        var proxy = GeneratedSource(
            runResult,
            GeneratorTestHelper.HintName(
                "Streaming.Single",
                "IUpload",
                GeneratorTestHelper.GeneratedKind.Proxy));

        proxy.Should().Contain("global::DotBoxD.Services.Streaming.Frames.RpcStreamAttachment __dotboxd_streams;");
        proxy.Should().Contain("__dotboxd_streams = global::DotBoxD.Services.Streaming.Frames.RpcStreamAttachment.FromStream");
        proxy.Should().Contain("__dotboxd_streams, ct)");
        proxy.Should().NotContain("global::DotBoxD.Services.Streaming.Frames.RpcStreamAttachment[] __dotboxd_streams;");
        proxy.Should().NotContain("__dotboxd_streams = new global::DotBoxD.Services.Streaming.Frames.RpcStreamAttachment[]");
    }

    private static string GeneratedSource(
        GeneratorDriverRunResult runResult,
        string hintName) =>
        runResult.Results.Single().GeneratedSources
            .Single(source => source.HintName == hintName)
            .SourceText
            .ToString();

    private static void EmitShouldSucceed(CSharpCompilation compilation)
    {
        using var ms = new MemoryStream();
        var emit = compilation.Emit(ms);
        emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("generated single-stream proxy code must compile");
    }
}
