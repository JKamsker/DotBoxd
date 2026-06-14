using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ShaRPC.SourceGenerator.Tests;
using Xunit;

namespace ShaRPC.SourceGenerator.Tests.Cov;

/// <summary>
/// Round 4 regression for <c>MethodModelFactory.EscapeCharLiteral</c>. The char-literal escaper emitted
/// U+2028 (LINE SEPARATOR) and U+2029 (PARAGRAPH SEPARATOR) raw: neither is a named escape, and
/// <see cref="char.IsControl(char)"/> returns <c>false</c> for both (their Unicode categories are
/// LineSeparator / ParagraphSeparator), so the fallthrough emitted <c>c.ToString()</c>. The C# compiler,
/// however, treats both as line terminators inside a character literal, so a generated proxy signature
/// such as <c>char sep = '&lt;U+2028&gt;'</c> fails to compile with CS1010 ("Newline in constant"). The
/// sibling <c>LiteralHelpers.EscapeStringLiteral</c> already escapes both code points; the char escaper
/// must too.
/// </summary>
public sealed class Round4_CharLiteralLineSeparatorEscapingTests
{
    private const int LineSeparator = 0x2028;       // U+2028 LINE SEPARATOR
    private const int ParagraphSeparator = 0x2029;  // U+2029 PARAGRAPH SEPARATOR

    [Fact]
    public void Generator_EscapesLineAndParagraphSeparatorCharDefaults_SoGeneratedCodeCompiles()
    {
        // Build the service source with the separators expressed as \uXXXX escapes so this test file stays
        // pure ASCII (no raw line terminators, which editors/git would mangle). Roslyn parses the escapes
        // back into the char constants the generator reflects as default values.
        var source =
            "using System.Threading;\n" +
            "using System.Threading.Tasks;\n" +
            "using ShaRPC.Core.Attributes;\n" +
            "namespace Bug.CharSep\n{\n" +
            "    [ShaRpcService]\n" +
            "    public interface ILineSepDefaults\n    {\n" +
            "        Task<int> LineAsync(char sep = '" + UnicodeEscape(LineSeparator) + "', CancellationToken ct = default);\n" +
            "        Task<int> ParaAsync(char sep = '" + UnicodeEscape(ParagraphSeparator) + "', CancellationToken ct = default);\n" +
            "    }\n}";

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var runResult = GeneratorTestHelper.CreateDriver().RunGenerators(compilation).GetRunResult();

        // The generator accepts these supported (if exotic) char defaults without reporting an error.
        Assert.Empty(runResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var proxy = GeneratedSource(runResult, "ShaRpcProxy");

        // The defaults must be emitted as \uXXXX escapes, never as the raw separator.
        Assert.Contains("sep = '" + UnicodeEscape(LineSeparator) + "'", proxy);
        Assert.Contains("sep = '" + UnicodeEscape(ParagraphSeparator) + "'", proxy);

        // The raw line/paragraph separators must not appear in the emitted source at all.
        Assert.DoesNotContain(((char)LineSeparator).ToString(), proxy);
        Assert.DoesNotContain(((char)ParagraphSeparator).ToString(), proxy);

        // The whole thing must compile: a raw separator inside a char literal is CS1010.
        var finalCompilation = ((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees);
        using var ms = new MemoryStream();
        var emit = finalCompilation.Emit(ms);
        Assert.True(
            emit.Success,
            string.Join(
                "\n",
                emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString())));
    }

    // Backslash built from its code point (0x5C) so the literal "\u" never appears in this file (tooling
    // would normalize it to the raw character). Produces a 6-char sequence such as backslash-u-2028.
    private static string UnicodeEscape(int codePoint) =>
        ((char)0x5C) + "u" + codePoint.ToString("x4");

    private static string GeneratedSource(GeneratorDriverRunResult runResult, string hintFragment) =>
        runResult.GeneratedTrees.First(t => t.FilePath.Contains(hintFragment)).GetText().ToString();
}
