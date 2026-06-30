using System.Collections.Immutable;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginAnalyzerLiveSettingTypeValidationTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public async Task Analyzer_rejects_task_like_live_setting_type()
    {
        var diagnostics = await AnalyzeAsync(TaskLikeLiveSettingSource);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK020" &&
                          diagnostic.GetMessage().Contains("Task<int>", StringComparison.Ordinal));
    }

    [Fact]
    public void Generator_rejects_task_like_live_setting_type()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(TaskLikeLiveSettingSource);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("Live setting", StringComparison.Ordinal));
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var compilation = CSharpCompilation.Create(
            "DotBoxDLiveSettingTypeValidationTest",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(DotBoxD.Abstractions.PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(DotBoxD.Kernels.SandboxModule).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(
            new DotBoxD.Plugins.Analyzer.Analysis.PluginAnalyzer());
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }

    private const string TaskLikeLiveSettingSource = """
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Plugins;

        namespace Sample;

        public sealed record DamageEvent(string TargetId);

        [Plugin("task-live-setting")]
        public sealed partial class DamageKernel : IEventKernel<DamageEvent>
        {
            [LiveSetting]
            public Task<int> MinDamage { get; set; } = null!;

            public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

            public void Handle(DamageEvent e, HookContext ctx)
                => ctx.Messages.Send(e.TargetId, "hit");
        }
        """;
}
