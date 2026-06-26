using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generation;

public sealed class GeneratorGuardTests
{
    [Fact]
    public void Unexpected_exception_becomes_DBXK117_diagnostic()
    {
        var tree = CSharpSyntaxTree.ParseText("class C { }");
        var root = tree.GetRoot();

        var result = GeneratorGuard.TryCreate<SyntaxNode, string>(
            "test stage",
            root,
            CancellationToken.None,
            static (_, _) => throw new InvalidOperationException("boom"),
            static node => node.GetLocation());

        Assert.False(result.HasValue);
        Assert.NotNull(result.Diagnostic);
        var diagnostic = result.Diagnostic!.ToDiagnostic();
        Assert.Equal("DBXK117", diagnostic.Id);
        Assert.Contains("test stage", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", diagnostic.GetMessage(), StringComparison.Ordinal);
    }
}
