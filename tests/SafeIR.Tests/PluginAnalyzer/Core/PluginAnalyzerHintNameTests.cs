using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SafeIR.PluginAnalyzer;
using SafeIR.Plugins;

namespace SafeIR.Tests;

public sealed class PluginAnalyzerHintNameTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public void Generator_uses_namespace_qualified_hint_names()
    {
        var result = RunGenerator("""
            using SafeIR.Plugins;
            using SafeIR.Server.Abstractions;

            namespace Alpha
            {

            public sealed record DamageEvent(string TargetId);

            [Plugin("alpha-damage")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, "alpha");
            }
            }

            namespace Beta
            {

            public sealed record DamageEvent(string TargetId);

            [Plugin("beta-damage")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, "beta");
            }
            }
            """);
        var hintNames = result.GeneratedTrees.Select(t => Path.GetFileName(t.FilePath)).ToArray();

        Assert.Equal(2, hintNames.Length);
        Assert.Equal(hintNames.Length, hintNames.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("Alpha.DamagePluginPackage.g.cs", hintNames);
        Assert.Contains("Beta.DamagePluginPackage.g.cs", hintNames);
    }

    [Fact]
    public void Generator_reports_duplicate_package_names_in_same_namespace()
    {
        var result = RunGenerator("""
            using SafeIR.Plugins;
            using SafeIR.Server.Abstractions;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            [Plugin("damage")]
            public sealed partial class Damage : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, "damage");
            }

            [Plugin("damage-kernel")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, "kernel");
            }
            """, expectGeneratorErrors: true);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "SGP100");
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void Generator_escapes_keyword_namespace_identifiers()
    {
        var result = RunGenerator("""
            using SafeIR.Plugins;
            using SafeIR.Server.Abstractions;

            namespace Sample.@event
            {

            public sealed record DamageEvent(string TargetId);

            [Plugin("keyword-namespace")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, "keyword");
            }
            }
            """);
        Assert.Null(result.Results[0].Exception);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity.Equals(DiagnosticSeverity.Error)));
        var tree = Assert.Single(result.GeneratedTrees);
        var generated = tree.GetText().ToString();

        Assert.Equal("Sample.event.DamagePluginPackage.g.cs", Path.GetFileName(tree.FilePath));
        Assert.Contains("namespace Sample.@event;", generated);
    }

    private static GeneratorDriverRunResult RunGenerator(string source, bool expectGeneratorErrors = false)
    {
        var compilation = CSharpCompilation.Create(
            "SafeIrPluginHintNameTest",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SafeIrPluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var diagnostics);

        if (!expectGeneratorErrors)
        {
            Assert.Empty(diagnostics.Where(d => d.Severity.Equals(DiagnosticSeverity.Error)));
        }

        Assert.Empty(outputCompilation.GetDiagnostics().Where(d => d.Severity.Equals(DiagnosticSeverity.Error)));
        return driver.GetRunResult();
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
