using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using PluginServer = DotBoxD.Plugins.PluginServer;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.HookChains;

public sealed class PluginAnalyzerHookChainNamespaceTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public void Hook_chain_package_uses_full_nested_block_namespace()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Outer
            {
                namespace Inner
                {
                    public sealed record AggroEvent(string TargetId, int Distance);

                    public static class Usage
                    {
                        public static void Configure(HookRegistry hooks)
                            => hooks.On<AggroEvent>()
                                .Where((e, ctx) => e.Distance <= 5)
                                .Run((e, ctx) => ctx.Messages.Send(e.TargetId, "calm"));
                    }
                }
            }
            """);

        var packageSource = Assert.Single(
            result.GeneratedTrees.Select(tree => tree.ToString()),
            source => source.Contains("public static class HookChain_", StringComparison.Ordinal));

        Assert.Contains("namespace Outer.Inner;", packageSource);
        Assert.DoesNotContain("namespace Inner;", packageSource);
    }

    private static GeneratorDriverRunResult RunGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "DotBoxDHookChainNamespaceGeneratorTest",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginServer).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        driver = driver.RunGenerators(compilation);
        return driver.GetRunResult();
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
