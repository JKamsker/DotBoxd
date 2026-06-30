using System.Collections.Immutable;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

/// <summary>
/// DBXK111 coverage: a recognized remote <c>RunLocal</c> chain whose <c>Where</c>/<c>Select</c> stages cannot be
/// lowered is skipped by the generator and would throw <see cref="System.NotSupportedException"/> at runtime. The
/// generator now reports DBXK111 (Info) so the cause is visible at build time instead of being a silent skip; a
/// chain that lowers reports nothing. Shares the fail-safe source constants + the <c>TrustedPlatformReferences</c>
/// helper with the other <see cref="RemoteRunLocalChainRuntimeTests"/> partials.
/// </summary>
public sealed partial class RemoteRunLocalChainRuntimeTests
{
    public static IEnumerable<object[]> UnlowerableRemoteRunLocalSources()
    {
        yield return [ConvertingCtorSource];   // constructor parameter type != field type
        yield return [InheritedDtoSource];     // projected DTO inherits a public property
        yield return [NonScalarEqualitySource]; // Where compares two non-scalar (list) operands
        yield return [DerivedFieldSource];     // projected DTO field derived in the constructor body
    }

    private const string ReorderedDtoConstructorEvaluationOrderSource = HostPrelude + """
        public sealed record OrderedPair(int First, int Second);

        public interface IOrderProbe
        {
            [HostBinding("order.probe.mark", "order.probe.mark", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
            int Mark(string value);
        }

        public static class ReorderedDtoConstructorUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.EncounterEvent>().Where(e => e.Distance <= 4)
                    .Select((e, ctx) => new OrderedPair(
                        Second: ctx.Host<IOrderProbe>().Mark("B"),
                        First: ctx.Host<IOrderProbe>().Mark("A")))
                    .RunLocal((pair, ctx) => { });
        }
        """;

    [Theory]
    [MemberData(nameof(UnlowerableRemoteRunLocalSources))]
    public void Unlowerable_remote_run_local_chain_reports_DBXK111(string source)
    {
        var diagnostic = Assert.Single(
            ChainGeneratorDiagnostics(source),
            d => string.Equals(d.Id, "DBXK111", StringComparison.Ordinal));
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
    }

    [Fact]
    public void Reordered_dto_constructor_arguments_report_DBXK111_with_evaluation_order_detail()
    {
        var diagnostic = Assert.Single(
            ChainGeneratorDiagnostics(ReorderedDtoConstructorEvaluationOrderSource),
            d => string.Equals(d.Id, "DBXK111", StringComparison.Ordinal));

        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
        Assert.Contains("evaluation order", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Lowerable_projection_chain_reports_no_DBXK111()
        => Assert.DoesNotContain(
            ChainGeneratorDiagnostics(RemoteRunLocalSource),
            d => string.Equals(d.Id, "DBXK111", StringComparison.Ordinal));

    [Fact]
    public void Lowerable_whole_event_chain_reports_no_DBXK111()
        => Assert.DoesNotContain(
            ChainGeneratorDiagnostics(RemoteWholeEventSource),
            d => string.Equals(d.Id, "DBXK111", StringComparison.Ordinal));

    private static ImmutableArray<Diagnostic> ChainGeneratorDiagnostics(string source)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview)
            .WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "DotBoxD.Plugins.Generated")]);
        var compilation = CSharpCompilation.Create(
            "DotBoxDRemoteRunLocalDiagnosticTest",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(ChainAggroEvent).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);
        return driver.RunGenerators(compilation).GetRunResult().Diagnostics;
    }
}
