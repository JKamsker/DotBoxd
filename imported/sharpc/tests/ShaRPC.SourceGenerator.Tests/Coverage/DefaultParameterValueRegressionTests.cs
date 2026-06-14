using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ShaRPC.SourceGenerator.Tests;
using Xunit;

namespace ShaRPC.SourceGenerator.Tests.Cov;

/// <summary>
/// Regression test for the bug-hunt finding that non-cancellation-token explicit default parameter
/// values were silently dropped from generated proxy and async-sibling signatures. The generator now
/// captures and re-emits the default-value literal.
/// </summary>
public sealed class DefaultParameterValueRegressionTests
{
    private const string Source = @"
using System.Threading;
using System.Threading.Tasks;
using ShaRPC.Core.Attributes;

namespace Bug.Reg
{
    public enum BugKind { First, Second, Third }

    [ShaRpcService]
    public interface IBugDefaults
    {
        Task<int> ComputeAsync(
            int required,
            string text = ""hi"",
            int count = 7,
            bool flag = true,
            BugKind kind = BugKind.Second,
            string note = null,
            long big = 9000000000L,
            uint mask = 4000000000,
            ulong huge = 18000000000000000000UL,
            short small = -3,
            byte b = 255,
            double ratio = 1.5,
            float ratioF = 2.5f,
            decimal price = 9.99m,
            char sep = '\n',
            int? maybe = null,
            BugKind? maybeKind = BugKind.Third,
            CancellationToken ct = default);
    }
}";

    [Fact]
    public void Generator_PreservesNonCancellationTokenDefaultParameterValues_OnProxyAndAsyncSibling()
    {
        var compilation = GeneratorTestHelper.CreateCompilation(Source);
        var runResult = GeneratorTestHelper.CreateDriver().RunGenerators(compilation).GetRunResult();

        // The generator must not report errors for this supported shape.
        Assert.Empty(runResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // The generated proxy preserves every non-CT default (previously all were dropped) and still
        // emits the cancellation-token default.
        var proxy = GeneratedSource(runResult, "ShaRpcProxy");
        Assert.Contains("text = \"hi\"", proxy);
        Assert.Contains("count = 7", proxy);
        Assert.Contains("flag = true", proxy);
        Assert.Contains("kind = (global::Bug.Reg.BugKind)1", proxy); // BugKind.Second
        Assert.Contains("note = null", proxy);
        Assert.Contains("big = 9000000000L", proxy);
        Assert.Contains("mask = 4000000000U", proxy);
        Assert.Contains("huge = 18000000000000000000UL", proxy);
        Assert.Contains("small = -3", proxy);
        Assert.Contains("b = 255", proxy);
        Assert.Contains("ratio = 1.5D", proxy);
        Assert.Contains("ratioF = 2.5F", proxy);
        Assert.Contains("price = 9.99M", proxy);
        Assert.Contains("sep = '\\n'", proxy);
        Assert.Contains("maybe = null", proxy);
        Assert.Contains("maybeKind = (global::Bug.Reg.BugKind)2", proxy); // BugKind.Third
        Assert.Contains("ct = default", proxy);

        // The generated async-sibling interface preserves them too.
        var asyncSibling = GeneratedSource(runResult, "ShaRpcAsync");
        Assert.Contains("text = \"hi\"", asyncSibling);
        Assert.Contains("count = 7", asyncSibling);
        Assert.Contains("kind = (global::Bug.Reg.BugKind)1", asyncSibling);

        // And the whole thing still compiles — the emitted defaults are valid C#.
        var finalCompilation = ((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees);
        using var ms = new MemoryStream();
        var emit = finalCompilation.Emit(ms);
        Assert.True(
            emit.Success,
            string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString())));
    }

    private static string GeneratedSource(GeneratorDriverRunResult runResult, string hintFragment) =>
        runResult.GeneratedTrees.First(t => t.FilePath.Contains(hintFragment)).GetText().ToString();
}
