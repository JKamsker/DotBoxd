using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ShaRPC.SourceGenerator.Tests;

public sealed class ValueTaskStreamingProxyRegressionTests
{
    [Fact]
    public void ValueTaskOfT_WithStreamedRequestParameter_UsesStreamAwareTaskOverload()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.IO;
            using System.Threading.Tasks;

            namespace Regress.ValueTaskStreams
            {
                [ShaRpcService]
                public interface IUpload
                {
                    ValueTask<int> CountAsync(Stream data);
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var finalCompilation = ((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees);

        using var ms = new MemoryStream();
        var emit = finalCompilation.Emit(ms);
        if (!emit.Success)
        {
            var errors = string.Join(
                Environment.NewLine,
                emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
            throw new InvalidOperationException(errors);
        }

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.ValueTaskStreams",
                "IUpload",
                GeneratorTestHelper.GeneratedKind.Proxy))
            .SourceText.ToString();

        proxy.Should().Contain("new global::System.Threading.Tasks.ValueTask<int>(");
        proxy.Should().Contain(
            "InvokeAsync<global::ShaRPC.Core.Protocol.RpcStreamHandle, int>(" +
            "\"IUpload\", \"CountAsync\"");
        proxy.Should().Contain("__sharpc_streams");
    }
}
