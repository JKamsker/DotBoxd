using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ShaRPC.SourceGenerator.Tests;
using Xunit;

namespace ShaRPC.SourceGenerator.Tests.Cov;

/// <summary>
/// Round 6 regression for <c>IdentifierHelpers.EscapeIdentifier</c>. It only consulted
/// <c>SyntaxFacts.GetKeywordKind</c>, which returns <c>None</c> for every C# CONTEXTUAL keyword
/// (<c>async</c>, <c>await</c>, <c>record</c>, …). A method named <c>async</c> emitted
/// <c>public async Task async(...)</c> — uncompilable (the parser reads <c>async Task</c> as the return
/// type). The escaper must also consult <c>GetContextualKeywordKind</c> so such identifiers get the
/// <c>@</c> prefix.
/// </summary>
public sealed class Round6_ContextualKeywordIdentifierTests
{
    private const string Source = @"
using System.Threading;
using System.Threading.Tasks;
using ShaRPC.Core.Attributes;

namespace Bug.ContextualKw
{
    [ShaRpcService]
    public interface IContextualKeywords
    {
        Task<int> @async(int @await, int @record, CancellationToken ct = default);
    }
}";

    [Fact]
    public void Generator_EscapesContextualKeywordIdentifiers_SoGeneratedCodeCompiles()
    {
        var compilation = GeneratorTestHelper.CreateCompilation(Source);
        var runResult = GeneratorTestHelper.CreateDriver().RunGenerators(compilation).GetRunResult();

        Assert.Empty(runResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // The generated proxy/dispatcher/async-sibling must compile. With the bug, a contextual-keyword
        // method/parameter name is emitted unescaped and the C# parser rejects it.
        var finalCompilation = ((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees);
        using var ms = new MemoryStream();
        var emit = finalCompilation.Emit(ms);
        Assert.True(
            emit.Success,
            string.Join(
                "\n",
                emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString())));
    }
}
