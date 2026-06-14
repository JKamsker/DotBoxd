using System.Linq;
using ShaRPC.SourceGenerator.Tests;
using Xunit;

namespace ShaRPC.SourceGenerator.Tests.Cov;

/// <summary>
/// Round2 regression test for DEFECT #12: wire-name / service-name collision diagnostics
/// display the C#-escaped form of the name instead of the actual runtime name.
///
/// <para>
/// <see cref="ShaRPC.SourceGenerator.MethodModel"/>.<c>RpcName</c> and
/// <see cref="ShaRPC.SourceGenerator.ServiceModel"/>.<c>ServiceName</c> store the value
/// already run through <c>LiteralHelpers.EscapeStringLiteral(...)</c> (MethodModelFactory
/// line ~153, ServiceModelFactory line ~187) because that value is later emitted inside a
/// generated string literal. The duplicate-wire-name (SHARPC002) and duplicate-service-name
/// (SHARPC003) diagnostics embed those stored names directly, so a runtime wire name
/// <c>foo\bar</c> (one backslash) is shown to the developer as <c>foo\\bar</c> (two
/// backslashes) — the escaped literal leaks into a human-facing message.
/// </para>
///
/// <para>
/// These tests assert the desired behaviour: the diagnostic message should contain the real
/// single-backslash name and must not contain the doubled-backslash escaped form. RED on the
/// current (unfixed) code, which embeds the escaped name. The additive fix (store a raw
/// pre-escape name alongside the escaped one and use it only in diagnostics) makes them green.
/// </para>
/// </summary>
public sealed class Round2_WireNameDiagnosticEscapingTests
{
    // Runtime wire name as the developer sees it: a single backslash between foo and bar.
    private const string RawWireName = "foo\\bar";

    // The escaped form the buggy code currently leaks into the message: two backslashes.
    private const string EscapedWireName = "foo\\\\bar";

    [Fact]
    public void DuplicateWireMethodName_Diagnostic_ShowsRawName_NotEscapedLiteral()
    {
        // Two methods that share the runtime wire name "foo\bar" (one backslash). In the raw
        // string literal below, "foo\\bar" is literal source text, so the embedded C# attribute
        // argument is the string "foo\bar" at runtime.
        const string source = """
            using ShaRPC.Core.Attributes;

            namespace Regress.WireMethodEscaping
            {
                [ShaRpcService]
                public interface IService
                {
                    [ShaRpcMethod(Name = "foo\\bar")]
                    int First();

                    [ShaRpcMethod(Name = "foo\\bar")]
                    int Second();
                }
            }
            """;

        var runResult = Run(source);

        var collisionDiagnostics = runResult.Diagnostics
            .Where(d => d.Id == "SHARPC002" &&
                        d.GetMessage().Contains("wire method name"))
            .ToList();

        Assert.Equal(2, collisionDiagnostics.Count);

        foreach (var diagnostic in collisionDiagnostics)
        {
            var message = diagnostic.GetMessage();

            Assert.Contains(
                RawWireName,
                message);

            Assert.DoesNotContain(
                EscapedWireName,
                message);
        }
    }

    [Fact]
    public void DuplicateWireServiceName_Diagnostic_ShowsRawName_NotEscapedLiteral()
    {
        // Two services that share the runtime wire name "foo\bar" (one backslash).
        const string source = """
            using ShaRPC.Core.Attributes;

            namespace Regress.WireServiceEscaping
            {
                [ShaRpcService(Name = "foo\\bar")]
                public interface IFoo
                {
                    int Foo();
                }

                [ShaRpcService(Name = "foo\\bar")]
                public interface IBar
                {
                    int Bar();
                }
            }
            """;

        var runResult = Run(source);

        var collisionDiagnostics = runResult.Diagnostics
            .Where(d => d.Id == "SHARPC003" &&
                        d.GetMessage().Contains("wire service name"))
            .ToList();

        Assert.Equal(2, collisionDiagnostics.Count);

        foreach (var diagnostic in collisionDiagnostics)
        {
            var message = diagnostic.GetMessage();

            Assert.Contains(
                RawWireName,
                message);

            Assert.DoesNotContain(
                EscapedWireName,
                message);
        }
    }

    private static Microsoft.CodeAnalysis.GeneratorDriverRunResult Run(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        return driver.GetRunResult();
    }
}
