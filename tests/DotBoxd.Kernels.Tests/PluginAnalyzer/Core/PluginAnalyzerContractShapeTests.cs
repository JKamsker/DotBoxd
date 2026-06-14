using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using DotBoxd.Plugins.Analyzer;
using DotBoxd.Plugins;

namespace DotBoxd.Kernels.Tests;

public sealed class PluginAnalyzerContractShapeTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public void Generator_reports_game_plugin_with_multiple_event_kernel_contracts()
    {
        var result = RunGenerator("""
            using DotBoxd.Plugins;
            using DotBoxd.Abstractions;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);
            public sealed record HealEvent(string TargetId);

            [Plugin("multiple-contracts")]
            public sealed partial class DamageKernel :
                IEventKernel<DamageEvent>,
                IEventKernel<HealEvent>
            {
                bool IEventKernel<DamageEvent>.ShouldHandle(DamageEvent e, HookContext ctx) => true;

                void IEventKernel<DamageEvent>.Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, "damage");

                bool IEventKernel<HealEvent>.ShouldHandle(HealEvent e, HookContext ctx) => true;

                void IEventKernel<HealEvent>.Handle(HealEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, "heal");
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Empty(result.GeneratedTrees);
    }

    private static GeneratorDriverRunResult RunGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "DotBoxdPluginContractShapeTest",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new DotBoxdPluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
