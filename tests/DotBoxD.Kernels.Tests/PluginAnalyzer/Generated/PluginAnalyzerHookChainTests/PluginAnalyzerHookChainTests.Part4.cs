using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using PluginServer = DotBoxD.Plugins.PluginServer;
namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginAnalyzerHookChainTests
{
    [Fact]
    public void Unlowered_Run_reports_DBXK114_as_warning()
    {
        var result = RunGenerator("""
            using System.Threading.Tasks;
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageEvent>()
                        .Run((e, ctx) =>
                        {
                            ctx.Messages.Send(e.TargetId, "damage");
                            return ValueTask.CompletedTask;
                        });
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK114"));
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("HookChain_", StringComparison.Ordinal));
    }

    [Fact]
    public void Unlowered_RegisterLocal_reports_DBXK113_as_info()
    {
        // RegisterLocal is an escape hatch whose body need not lower; a not-lowered case stays Info, consistent
        // with the remote RunLocal (DBXK111) not-lowered diagnostic.
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record NoHookCtx(int Damage);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Damage);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<NoHookCtx>()
                        .RegisterLocal((ctx, hookContext) => new DamageResult { Success = true, Damage = ctx.Damage }, 0);
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK113"));
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
    }

    [Fact]
    public void Remote_staged_Use_reports_DBXK100_error_instead_of_installing_unfiltered_package()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);
            public sealed class DamageKernel;

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                    => hooks.On<DamageEvent>()
                        .Where(e => e.TargetId == "monster-1")
                        .Use<DamageKernel>();
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK100"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Where/Select", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("Use", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Remote_subscription_staged_Use_reports_DBXK100_error()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);
            public sealed class DamageKernel;

            public static class Usage
            {
                public static void Configure(RemoteSubscriptionRegistry subscriptions)
                    => subscriptions.On<DamageEvent>()
                        .Where(e => e.TargetId == "monster-1")
                        .Use<DamageKernel>();
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK100"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Where/Select", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("Use", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    private static GeneratorDriverRunResult RunGenerator(string source)
        => RunGeneratorCore(source).Result;

    private static Compilation RunGeneratorCompilation(string source)
        => RunGeneratorCore(source).Output;

    private static (Compilation Output, GeneratorDriverRunResult Result) RunGeneratorCore(string source)
    {
        var compilation = CSharpCompilation.Create(
            "DotBoxDHookChainGeneratorTest",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginServer).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return (output, driver.GetRunResult());
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }

}
