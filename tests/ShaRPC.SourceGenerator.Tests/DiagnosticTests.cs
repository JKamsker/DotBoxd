using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ShaRPC.SourceGenerator.Tests;

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
            using ShaRPC.Core.Attributes;

            namespace Diag.Empty
            {
                [ShaRpcService]
                public interface IEmpty
                {
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        // No SHARPC001 errors.
        runResult.Diagnostics.Should().NotContain(d => d.Id == "SHARPC001" && d.Severity == DiagnosticSeverity.Error);

        var hints = runResult.Results.Single().GeneratedSources.Select(g => g.HintName).ToArray();
        hints.Should().Contain("IEmpty.ShaRpcProxy.g.cs");
        hints.Should().Contain("IEmpty.ShaRpcDispatcher.g.cs");
        hints.Should().Contain("ShaRpcExtensions.g.cs");

        // The combined compilation should emit successfully.
        var finalCompilation = ((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees);
        using var ms = new MemoryStream();
        var emit = finalCompilation.Emit(ms);
        emit.Success.Should().BeTrue("empty service should still emit successfully. Errors: " +
            string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString())));
    }

    [Fact]
    public void ServiceWithUnresolvableMethodSignature_DoesNotCrashAndStillEmitsAFile()
    {
        // This source references a type the user hasn't declared (UnknownType). The
        // generator must still produce per-service output files (proxy/dispatcher) — the
        // user's project may add the missing type from another file or via referenced
        // assemblies, so the generator must not silently drop the service. It must also
        // not surface its own NullReferenceException via SHARPC001.
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Diag.Broken
            {
                [ShaRpcService]
                public interface IBroken
                {
                    Task<UnknownType> DoSomethingAsync(UnknownType input);
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        // No SHARPC001 escaping with an internal NRE — generator must handle error symbols.
        runResult.Diagnostics
            .Where(d => d.Id == "SHARPC001")
            .Should().NotContain(d => d.GetMessage().Contains("NullReferenceException"),
                "the generator must not propagate its own NREs through SHARPC001");

        // Positive assertion: per-service hint names must still be produced.
        var hints = runResult.Results.Single().GeneratedSources.Select(g => g.HintName).ToArray();
        hints.Should().Contain("IBroken.ShaRpcProxy.g.cs",
            "the generator should still emit a proxy hint for IBroken so consumers see something");
        hints.Should().Contain("IBroken.ShaRpcDispatcher.g.cs");
    }
}
