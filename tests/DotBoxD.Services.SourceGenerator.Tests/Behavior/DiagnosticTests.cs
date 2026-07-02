using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Behavior;

/// <summary>
/// Negative-path tests: malformed user code must never crash the generator and
/// degenerate-but-valid services must still produce compilable output.
/// </summary>
public class DiagnosticTests
{
    [Fact]
    public void EmptyServiceInterface_StillGeneratesCompilableOutput()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace Diag.Empty
            {
                [RpcService]
                public interface IEmpty
                {
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        // No DBXS001 errors.
        runResult.Diagnostics.Should().NotContain(d => d.Id == "DBXS001" && d.Severity == DiagnosticSeverity.Error);

        var hints = runResult.Results.Single().GeneratedSources.Select(g => g.HintName).ToArray();
        hints.Should().Contain("Diag_Empty_IEmpty.DotBoxDRpcProxy.g.cs");
        hints.Should().Contain("Diag_Empty_IEmpty.DotBoxDRpcDispatcher.g.cs");
        hints.Should().Contain("DotBoxDRpcExtensions.g.cs");

        // The combined compilation should emit successfully.
        var finalCompilation = ((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees);
        using var ms = new MemoryStream();
        var emit = finalCompilation.Emit(ms);
        emit.Success.Should().BeTrue("empty service should still emit successfully. Errors: " +
            string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString())));
    }

    [Fact]
    public void LegacyServiceAndMethodAttributes_StillGenerateForCompatibilityWindow()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace Diag.Legacy
            {
            #pragma warning disable CS0618
                [DotBoxDService(Name = "LegacyService")]
                public interface ILegacy
                {
                    [DotBoxDMethod(Name = "LegacyPing")]
                    void Ping();
                }
            #pragma warning restore CS0618
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        runResult.Diagnostics.Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);
        var hints = runResult.Results.Single().GeneratedSources.Select(g => g.HintName).ToArray();
        hints.Should().Contain("Diag_Legacy_ILegacy.DotBoxDRpcProxy.g.cs");
        hints.Should().Contain("Diag_Legacy_ILegacy.DotBoxDRpcDispatcher.g.cs");

        var generated = string.Join("\n", runResult.GeneratedTrees.Select(tree => tree.ToString()));
        generated.Should().Contain("\"LegacyService\"");
        generated.Should().Contain("\"LegacyPing\"");
    }

    [Fact]
    public void ServiceWithUnresolvableMethodSignature_DoesNotCrashAndStillEmitsAFile()
    {
        // This source references a type the user hasn't declared (UnknownType). The
        // generator must still produce per-service output files (proxy/dispatcher) — the
        // user's project may add the missing type from another file or via referenced
        // assemblies, so the generator must not silently drop the service. It must also
        // not surface its own NullReferenceException via DBXS001.
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Diag.Broken
            {
                [RpcService]
                public interface IBroken
                {
                    Task<UnknownType> DoSomethingAsync(UnknownType input);
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        // No DBXS001 escaping with an internal NRE — generator must handle error symbols.
        runResult.Diagnostics
            .Where(d => d.Id == "DBXS001")
            .Should().NotContain(d => d.GetMessage().Contains("NullReferenceException"),
                "the generator must not propagate its own NREs through DBXS001");

        // Positive assertion: per-service hint names must still be produced.
        var hints = runResult.Results.Single().GeneratedSources.Select(g => g.HintName).ToArray();
        hints.Should().Contain("Diag_Broken_IBroken.DotBoxDRpcProxy.g.cs",
            "the generator should still emit a proxy hint for IBroken so consumers see something");
        hints.Should().Contain("Diag_Broken_IBroken.DotBoxDRpcDispatcher.g.cs");
    }

    [Fact]
    public void MethodDiagnostics_ReportSourceLocation()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace Diag.Location
            {
                [RpcService]
                public interface IRefParam
                {
                    void Bad(ref int value);
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var diagnostic = driver.GetRunResult().Diagnostics.Single(d => d.Id == "DBXS002");

        diagnostic.Location.Should().NotBe(Location.None);
        diagnostic.Location.GetLineSpan().StartLinePosition.Line.Should().BeGreaterThan(0);
        GetDiagnosticLine(source, diagnostic)
            .Substring(diagnostic.Location.GetLineSpan().StartLinePosition.Character)
            .Should().StartWith("value");
    }

    [Fact]
    public void ServiceDiagnostics_ReportSourceLocation()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace Diag.Location
            {
                [RpcService]
                public interface IWithProperty
                {
                    int Count { get; }
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var diagnostic = driver.GetRunResult().Diagnostics.Single(d => d.Id == "DBXS003");

        diagnostic.Location.Should().NotBe(Location.None);
        diagnostic.Location.GetLineSpan().StartLinePosition.Line.Should().BeGreaterThan(0);
        GetDiagnosticLine(source, diagnostic)
            .Substring(diagnostic.Location.GetLineSpan().StartLinePosition.Character)
            .Should().StartWith("Count");
    }

    private static string GetDiagnosticLine(string source, Diagnostic diagnostic)
    {
        var line = diagnostic.Location.GetLineSpan().StartLinePosition.Line;
        return source.Replace("\r\n", "\n").Split('\n')[line];
    }
}
