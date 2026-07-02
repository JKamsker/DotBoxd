using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginAnalyzerTests
{
    [Theory]
    [InlineData("Plugin(\"generic-plugin\")")]
    [InlineData("EventKernel(\"generic-event\")")]
    public void Generator_rejects_generic_package_owner_kernels(string attribute)
    {
        var compilation = CreateCompilation($$"""
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record DamageEvent(string DamageType);

            [{{attribute}}]
            public sealed partial class DamageKernel<T> : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send("player-1", e.DamageType);
            }
            """);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out _,
            out var generatorDiagnostics);

        Assert.Contains(
            generatorDiagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("generic", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(driver.GetRunResult().GeneratedTrees);
    }
}
