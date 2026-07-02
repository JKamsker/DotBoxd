using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

/// <summary>
/// Reachability holes in the DBXK001 forbidden-host-API analyzer found in the 2026-06-26 surprise hunt.
/// A forbidden API reachable from an event kernel must be reported regardless of whether it is reached
/// through a method-body invocation, a field/property initializer, a method group/delegate, or a helper
/// property getter. Each of these was previously silently un-analyzed (fail-open).
/// </summary>
public sealed class PluginAnalyzerForbiddenApiReachabilityTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    // #16 — forbidden host API in a field initializer inside an event kernel.
    [Fact]
    public async Task Reports_forbidden_api_in_field_initializer()
    {
        var diagnostics = await AnalyzeAsync("""
            namespace Sample
            {
                using DotBoxD.Plugins;
                using DotBoxD.Abstractions;

                [Plugin("init-leak")]
                public sealed class InitKernel : IEventKernel<string>
                {
                    private static readonly int Leaked = System.IO.File.ReadAllText("/etc/passwd").Length;

                    public bool ShouldHandle(string e, HookContext context) => Leaked >= 0;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "DBXK001");
    }

    // #16 — forbidden host API in a property initializer inside an event kernel.
    [Fact]
    public async Task Reports_forbidden_api_in_property_initializer()
    {
        var diagnostics = await AnalyzeAsync("""
            namespace Sample
            {
                using DotBoxD.Plugins;
                using DotBoxD.Abstractions;

                [Plugin("prop-init-leak")]
                public sealed class PropInitKernel : IEventKernel<string>
                {
                    private int Leaked { get; } = System.IO.File.ReadAllText("/etc/passwd").Length;

                    public bool ShouldHandle(string e, HookContext context) => Leaked >= 0;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "DBXK001");
    }

    // #17 — forbidden helper reached via a method group passed to a delegate parameter.
    [Fact]
    public async Task Reports_forbidden_helper_reached_via_method_group()
    {
        var diagnostics = await AnalyzeAsync("""
            namespace Sample
            {
                using System.Linq;
                using DotBoxD.Plugins;
                using DotBoxD.Abstractions;

                public static class Helper
                {
                    public static int Danger(int x) => System.IO.File.ReadAllText("/x").Length + x;
                }

                [Plugin("method-group-leak")]
                public sealed class MgKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context)
                        => new[] { 1 }.Select(Helper.Danger).Any(v => v > 0);

                    public void Handle(string e, HookContext context) { }
                }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "DBXK001");
    }

    // #18 — forbidden host API behind a helper property getter read from an event kernel.
    [Fact]
    public async Task Reports_forbidden_api_behind_helper_property_getter()
    {
        var diagnostics = await AnalyzeAsync("""
            namespace Sample
            {
                using DotBoxD.Plugins;
                using DotBoxD.Abstractions;

                public static class Helper
                {
                    public static int Danger => System.IO.File.ReadAllText("/x").Length;
                }

                [Plugin("property-getter-leak")]
                public sealed class PgKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context) => Helper.Danger > 0;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "DBXK001");
    }

    // A field initializer that calls a helper whose body transitively reaches a forbidden host API. The
    // initializer runs when the kernel is constructed in-host, so it must be a call-graph ROOT just like a
    // method-body call; previously initializers only reported a DIRECT forbidden type, so this was fail-open.
    [Fact]
    public async Task Reports_forbidden_helper_reached_through_field_initializer()
    {
        var diagnostics = await AnalyzeAsync("""
            namespace Sample
            {
                using DotBoxD.Plugins;
                using DotBoxD.Abstractions;

                public static class Helper
                {
                    public static int Danger() => System.IO.File.ReadAllText("/etc/passwd").Length;
                }

                [Plugin("init-helper-leak")]
                public sealed class InitHelperKernel : IEventKernel<string>
                {
                    private static readonly int Leaked = Helper.Danger();

                    public bool ShouldHandle(string e, HookContext context) => Leaked >= 0;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "DBXK001");
    }

    // A property initializer that reads a helper property getter whose body transitively reaches a forbidden
    // host API must also be reported (the getter accessor is the call-graph edge, mirroring the method-body path).
    [Fact]
    public async Task Reports_forbidden_helper_getter_reached_through_property_initializer()
    {
        var diagnostics = await AnalyzeAsync("""
            namespace Sample
            {
                using DotBoxD.Plugins;
                using DotBoxD.Abstractions;

                public static class Helper
                {
                    public static int Danger => System.IO.File.ReadAllText("/x").Length;
                }

                [Plugin("prop-init-helper-leak")]
                public sealed class PropInitHelperKernel : IEventKernel<string>
                {
                    private int Leaked { get; } = Helper.Danger;

                    public bool ShouldHandle(string e, HookContext context) => Leaked >= 0;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "DBXK001");
    }

    [Fact]
    public async Task Reports_forbidden_field_reference_in_field_initializer()
    {
        var diagnostics = await AnalyzeAsync("""
            namespace Sample
            {
                using DotBoxD.Plugins;
                using DotBoxD.Abstractions;

                [Plugin("field-ref-init-leak")]
                public sealed class FieldRefInitKernel : IEventKernel<string>
                {
                    private static readonly object Leaked = System.Reflection.BindingFlags.Public;

                    public bool ShouldHandle(string e, HookContext context) => Leaked is not null;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "DBXK001");
    }

    [Fact]
    public async Task Reports_forbidden_typeof_in_field_initializer()
    {
        var diagnostics = await AnalyzeAsync("""
            namespace Sample
            {
                using System;
                using DotBoxD.Plugins;
                using DotBoxD.Abstractions;

                [Plugin("typeof-init-leak")]
                public sealed class TypeOfInitKernel : IEventKernel<string>
                {
                    private static readonly Type Leaked = typeof(System.IO.File);

                    public bool ShouldHandle(string e, HookContext context) => Leaked is not null;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "DBXK001");
    }

    // A benign helper used in a field initializer (no forbidden API anywhere in its transitive body) must NOT
    // be reported: modeling initializers as roots must not turn every helper call into a false positive.
    [Fact]
    public async Task Does_not_report_benign_helper_in_field_initializer()
    {
        var diagnostics = await AnalyzeAsync("""
            namespace Sample
            {
                using DotBoxD.Plugins;
                using DotBoxD.Abstractions;

                public static class Helper
                {
                    public static int Compute() => 40 + 2;
                }

                [Plugin("init-benign")]
                public sealed class BenignInitKernel : IEventKernel<string>
                {
                    private static readonly int Value = Helper.Compute();

                    public bool ShouldHandle(string e, HookContext context) => Value >= 0;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id == "DBXK001");
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var compilation = CreateCompilation(source);
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new DotBoxD.Plugins.Analyzer.Analysis.PluginAnalyzer());
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions);
        return CSharpCompilation.Create(
            "DotBoxDPluginAnalyzerForbiddenReachabilityTest",
            [syntaxTree],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
