using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SafeIR;
using SafeIR.PluginAnalyzer;
using SafeIR.Plugins;

namespace SafeIR.Tests;

/// <summary>An event type a generated hook chain subscribes to (referenced from the chain source).</summary>
public sealed record ChainAggroEvent(string MonsterId, int Distance);

/// <summary>
/// End-to-end runtime proof of the Phase C lowering + hook-up: a real inline chain is lowered by the
/// generator, compiled, loaded, installed via <see cref="HookPipeline{TEvent}.UseGeneratedChain"/>, and
/// the lowered verified IR executes — its <c>Where</c> gates and its <c>Send</c> runs exactly as the
/// source described. This is what a generated interceptor at the <c>InvokeKernel</c> call site performs.
/// </summary>
public sealed class HookChainRuntimeTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public async Task A_lowered_Where_chain_runs_only_when_its_condition_holds()
    {
        var package = LoadChainPackage("""
            using SafeIR.Plugins;

            namespace ChainSample;

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<global::SafeIR.Tests.ChainAggroEvent>()
                        .Where((e, ctx) => e.Distance <= 5)
                        .InvokeKernel((e, ctx) => ctx.Messages.Send(e.MonsterId, "calm"));
            }
            """);

        var messages = new InMemoryPluginMessageSink();
        using var server = PluginServer.Create(messages, defaultPolicy: ChainPolicy());

        // What the generated interceptor does at the InvokeKernel call site:
        server.Hooks.On<ChainAggroEvent>().UseGeneratedChain(package);

        await server.Hooks.PublishAsync(new ChainAggroEvent("monster-1", 3));   // 3 <= 5 → fires
        await server.Hooks.PublishAsync(new ChainAggroEvent("monster-2", 10));  // 10 > 5 → skipped

        var message = Assert.Single(messages.Messages);
        Assert.Equal("monster-1", message.TargetId);
        Assert.Equal("calm", message.Message);
    }

    private static PluginPackage LoadChainPackage(string source)
    {
        var compilation = CSharpCompilation.Create(
            "SafeIrChainRuntimeTest",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(ChainAggroEvent).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SafeIrPluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));

        using var stream = new MemoryStream();
        var emit = output.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics.Select(d => d.ToString())));

        var loaded = Assembly.Load(stream.ToArray());
        var packageType = loaded.GetTypes().Single(type =>
            type.Name.StartsWith("HookChain_", StringComparison.Ordinal) &&
            type.Name.EndsWith("PluginPackage", StringComparison.Ordinal));
        var create = packageType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!;
        return (PluginPackage)create.Invoke(null, null)!;
    }

    private static SandboxPolicy ChainPolicy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .GrantHostMessageWrite()
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
