using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Plugins.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using DiagnosticSeverity = DotBoxD.Kernels.Model.DiagnosticSeverity;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginAnalyzerPropertyShapeTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public void Generator_ignores_event_indexers_like_convention_adapter()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed class DamageEvent
            {
                public DamageEvent(string targetId, string message)
                {
                    TargetId = targetId;
                    Message = message;
                }

                public string TargetId { get; }
                public string Message { get; }
                public string this[int index] => "ignored";
            }

            [Plugin("event-indexer")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.Message);
            }
            """);
        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();

        Assert.Contains("e_TargetId", generated);
        Assert.Contains("e_Message", generated);
        Assert.DoesNotContain("e_Item", generated);
    }

    [Fact]
    public void Generator_ignores_event_properties_without_public_getters()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed class DamageEvent
            {
                public string TargetId { get; } = "player-1";
                public string PrivateGetter { private get; set; } = "hidden";
            }

            [Plugin("event-private-getter")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, "message");
            }
            """);
        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();

        Assert.Contains("e_TargetId", generated);
        Assert.DoesNotContain("e_PrivateGetter", generated);
    }

    [Fact]
    public void Convention_adapter_ignores_event_properties_without_public_getters()
    {
        var adapter = new PluginEventAdapterRegistry().Resolve<PrivateGetterEvent>();

        var parameter = Assert.Single(adapter.Parameters);
        var value = Assert.Single(adapter.ToSandboxValues(new PrivateGetterEvent()));
        Assert.Equal("e_TargetId", parameter.Name);
        Assert.Equal("player-1", ((StringValue)value).Value);
    }

    [Fact]
    public void Convention_adapter_rejects_duplicate_event_property_names()
    {
        Assert.Throws<NotSupportedException>(
            () => new PluginEventAdapterRegistry().Resolve<DuplicateCaseEvent>());
    }

    [Fact]
    public void Convention_adapter_encodes_null_nullable_scalar_event_properties()
    {
        var adapter = new PluginEventAdapterRegistry().Resolve<NullableScalarEvent>();

        var value = Assert.Single(adapter.ToSandboxValues(new NullableScalarEvent(null)));
        var record = Assert.IsType<RecordValue>(value);

        Assert.Equal([SandboxValue.FromBool(false), SandboxValue.FromInt32(0)], record.Fields);
    }

    [Fact]
    public void Generator_rejects_nullable_string_event_properties()
    {
        var result = RunGenerator("""
            #nullable enable

            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record NullableStringEvent(string? TargetId);

            [Plugin("nullable-string-event")]
            public sealed partial class NullableStringKernel : IEventKernel<NullableStringEvent>
            {
                public bool ShouldHandle(NullableStringEvent e, HookContext ctx) => true;

                public void Handle(NullableStringEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, "message");
            }
            """, expectGeneratorErrors: true);

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("TargetId", StringComparison.Ordinal) &&
                          diagnostic.GetMessage().Contains("nullable reference", StringComparison.Ordinal));
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void Generator_rejects_live_setting_indexers()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            [Plugin("live-indexer")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                [LiveSetting]
                public int this[int index]
                {
                    get => 0;
                    set { }
                }

                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, "message");
            }
            """, expectGeneratorErrors: true);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void Generator_rejects_live_settings_that_collide_with_generated_event_parameters()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record DamageEvent(string TargetId, string Message);

            [Plugin("parameter-collision")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                [LiveSetting]
                public string e_Message { get; set; } = "setting";

                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.Message);
            }
            """, expectGeneratorErrors: true);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void Generator_rejects_duplicate_event_property_names()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public class BaseDamageEvent
            {
                public string Message { get; } = "";
            }

            public sealed class DamageEvent : BaseDamageEvent
            {
                public string TargetId { get; } = "";
                public new string Message { get; } = "";
            }

            [Plugin("duplicate-event-property")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.Message);
            }
            """, expectGeneratorErrors: true);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Empty(result.GeneratedTrees);
    }

    private static GeneratorDriverRunResult RunGenerator(string source, bool expectGeneratorErrors = false)
    {
        var compilation = CSharpCompilation.Create(
            "DotBoxDPluginPropertyShapeTest",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
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

    private sealed class PrivateGetterEvent
    {
        public string TargetId { get; } = "player-1";

        public string PrivateGetter { private get; set; } = "hidden";
    }

    private sealed class DuplicateCaseEvent
    {
        public string TargetId { get; } = "player-1";

        public string targetId { get; } = "player-2";
    }

    private sealed record NullableScalarEvent(int? Damage);
}
