using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Tests.Coverage;

/// <summary>
/// Regression test for source-defined metadata defaults on supported service methods. A user can
/// declare an optional <see cref="DateTime"/> parameter via <c>[Optional, DateTimeConstant]</c>,
/// so the generated proxy and async sibling should preserve the same callable surface instead of
/// silently dropping the default from their public signatures.
/// </summary>
public sealed class DateTimeConstantDefaultParameterRegressionTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices;
        using DotBoxD.Services.Attributes;

        namespace Bug.Reg;

        [DotBoxDService]
        public interface IClock
        {
            void Ping([Optional, DateTimeConstant(0L)] DateTime when);
        }
        """;

    [Fact]
    public void Generator_PreservesSourceDefinedDateTimeConstantDefaults_OnProxyAndAsyncSibling()
    {
        var compilation = GeneratorTestHelper.CreateCompilation(Source);
        var runResult = GeneratorTestHelper.CreateDriver().RunGenerators(compilation).GetRunResult();

        Assert.Empty(runResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var proxy = GeneratedSource(runResult, "DotBoxDRpcProxy");
        Assert.Contains("void Ping(global::System.DateTime when = default)", proxy);

        var asyncSibling = GeneratedSource(runResult, "DotBoxDRpcAsync");
        Assert.Contains(
            "global::System.Threading.Tasks.Task PingAsync(global::System.DateTime when = default, global::System.Threading.CancellationToken ct = default);",
            asyncSibling);
    }

    private static string GeneratedSource(GeneratorDriverRunResult runResult, string hintFragment) =>
        runResult.GeneratedTrees.First(t => t.FilePath.Contains(hintFragment)).GetText().ToString();
}
