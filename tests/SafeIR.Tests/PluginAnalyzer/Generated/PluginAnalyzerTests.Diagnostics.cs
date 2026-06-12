using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SafeIR;
using SafeIR.PluginAnalyzer;
using SafeIR.Plugins;

namespace SafeIR.Tests;

public sealed partial class PluginAnalyzerTests
{
    private static readonly CSharpParseOptions ParseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public async Task Reports_unsupported_live_setting_type()
    {
        var diagnostics = await AnalyzeAsync("""
            using SafeIR.Plugins;

            [GamePlugin("bad")]
            public sealed class BadKernel : IEventKernel<string>
            {
                [LiveSetting]
                public decimal Anything { get; set; } = 1m;

                public bool ShouldHandle(string e, HookContext context) => true;

                public void Handle(string e, HookContext context) { }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "SGP020");
    }

    [Fact]
    public async Task Reports_file_io_inside_event_kernel()
    {
        var diagnostics = await AnalyzeAsync("""
            using SafeIR.Plugins;

            [GamePlugin("bad")]
            public sealed class BadKernel : IEventKernel<string>
            {
                public bool ShouldHandle(string e, HookContext context)
                {
                    System.IO.File.WriteAllText("x.txt", "bad");
                    return true;
                }

                public void Handle(string e, HookContext context) { }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "SGP001");
    }

    [Theory]
    [InlineData("new System.Net.Http.HttpClient();")]
    [InlineData("System.Diagnostics.Process.Start(\"cmd.exe\");")]
    [InlineData("System.Threading.Tasks.Task.Run(() => { });")]
    [InlineData("System.Threading.Thread.Sleep(1);")]
    [InlineData("System.Environment.GetEnvironmentVariable(\"SECRET\");")]
    [InlineData("((System.IServiceProvider)null!).GetService(typeof(string));")]
    [InlineData("System.IO.Stream.Synchronized(null!);")]
    [InlineData("System.Reflection.Assembly.Load(\"System.Private.CoreLib\");")]
    [InlineData("var t = typeof(string);")]
    public async Task Reports_forbidden_host_apis_inside_event_kernel(string statement)
    {
        var diagnostics = await AnalyzeAsync($$"""
            using SafeIR.Plugins;

            [GamePlugin("bad")]
            public sealed class BadKernel : IEventKernel<string>
            {
                public bool ShouldHandle(string e, HookContext context)
                {
                    {{statement}}
                    return true;
                }

                public void Handle(string e, HookContext context) { }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "SGP001");
    }

    [Fact]
    public async Task Reports_forbidden_host_api_hidden_behind_helper_call()
    {
        var diagnostics = await AnalyzeAsync("""
            using SafeIR.Plugins;

            public static class BadHelper
            {
                public static void Write() => System.IO.File.WriteAllText("x.txt", "bad");
            }

            [GamePlugin("bad")]
            public sealed class BadKernel : IEventKernel<string>
            {
                public bool ShouldHandle(string e, HookContext context)
                {
                    BadHelper.Write();
                    return true;
                }

                public void Handle(string e, HookContext context) { }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "SGP001");
    }

    [Fact]
    public async Task Reports_forbidden_host_api_hidden_behind_deep_helper_chain()
    {
        var diagnostics = await AnalyzeAsync("""
            using SafeIR.Plugins;

            public static class BadHelper
            {
                public static void Step0() => Step1();
                public static void Step1() => Step2();
                public static void Step2() => Step3();
                public static void Step3() => Step4();
                public static void Step4() => System.IO.File.WriteAllText("x.txt", "bad");
            }

            [GamePlugin("bad")]
            public sealed class BadKernel : IEventKernel<string>
            {
                public bool ShouldHandle(string e, HookContext context)
                {
                    BadHelper.Step0();
                    return true;
                }

                public void Handle(string e, HookContext context) { }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "SGP001");
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var compilation = CreateCompilation(source);
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new SafeIrPluginAnalyzer());
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            ParseOptions);
        return CSharpCompilation.Create(
            "SafeIrPluginAnalyzerTest",
            [syntaxTree],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(GamePluginAttribute).Assembly.Location))
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
