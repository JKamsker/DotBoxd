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
/// End-to-end runtime proof of the Phase C lowering + interceptor hook-up: a real inline chain is
/// lowered by the generator, compiled, loaded, and the lowered verified IR executes correctly — its
/// <c>Where</c> gates and its <c>Send</c> runs. One test installs the package directly via
/// <see cref="HookPipeline{TEvent}.UseGeneratedChain"/>; the other proves the generated C# interceptor
/// does it automatically at the <c>InvokeKernel</c> call site (no manual wiring).
/// </summary>
public sealed class HookChainRuntimeTests
{
    private const string ChainSource = """
        using SafeIR.Plugins;

        namespace ChainSample;

        public static class Usage
        {
            public static void Configure(HookRegistry hooks)
                => hooks.On<global::SafeIR.Tests.ChainAggroEvent>()
                    .Where((e, ctx) => e.Distance <= 5)
                    .InvokeKernel((e, ctx) => ctx.Messages.Send(e.MonsterId, "calm"));
        }
        """;

    // A one-parameter Where (no context) lowers and runs exactly like the (e, ctx) form.
    private const string OneParamChainSource = """
        using SafeIR.Plugins;

        namespace ChainSample;

        public static class Usage
        {
            public static void Configure(HookRegistry hooks)
                => hooks.On<global::SafeIR.Tests.ChainAggroEvent>()
                    .Where(e => e.Distance <= 5)
                    .InvokeKernel((e, ctx) => ctx.Messages.Send(e.MonsterId, "calm"));
        }
        """;

    [Fact]
    public async Task A_lowered_one_parameter_Where_chain_runs_only_when_its_condition_holds()
    {
        var assembly = Compile(OneParamChainSource, enableInterceptors: true);
        var packageType = assembly.GetTypes().Single(type =>
            type.Name.StartsWith("HookChain_", StringComparison.Ordinal) &&
            type.Name.EndsWith("PluginPackage", StringComparison.Ordinal));
        var package = (PluginPackage)packageType
            .GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, null)!;

        var messages = new InMemoryPluginMessageSink();
        using var server = PluginServer.Create(messages, defaultPolicy: ChainPolicy());
        server.Hooks.On<ChainAggroEvent>().UseGeneratedChain(package);

        await server.Hooks.PublishAsync(new ChainAggroEvent("monster-1", 3));   // 3 <= 5 → fires
        await server.Hooks.PublishAsync(new ChainAggroEvent("monster-2", 10));  // 10 > 5 → skipped

        var message = Assert.Single(messages.Messages);
        Assert.Equal("monster-1", message.TargetId);
        Assert.Equal("calm", message.Message);
    }

    // A one-parameter Select projects the element; the projection must flow into the lowered terminal.
    private const string OneParamSelectChainSource = """
        using SafeIR.Plugins;

        namespace ChainSample;

        public static class Usage
        {
            public static void Configure(HookRegistry hooks)
                => hooks.On<global::SafeIR.Tests.ChainAggroEvent>()
                    .Select(e => e.MonsterId)
                    .InvokeKernel((id, ctx) => ctx.Messages.Send(id, "calm"));
        }
        """;

    [Fact]
    public async Task A_lowered_one_parameter_Select_projects_into_the_terminal_send_target()
    {
        var assembly = Compile(OneParamSelectChainSource, enableInterceptors: true);
        var packageType = assembly.GetTypes().Single(type =>
            type.Name.StartsWith("HookChain_", StringComparison.Ordinal) &&
            type.Name.EndsWith("PluginPackage", StringComparison.Ordinal));
        var package = (PluginPackage)packageType
            .GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, null)!;

        var messages = new InMemoryPluginMessageSink();
        using var server = PluginServer.Create(messages, defaultPolicy: ChainPolicy());
        server.Hooks.On<ChainAggroEvent>().UseGeneratedChain(package);

        await server.Hooks.PublishAsync(new ChainAggroEvent("monster-7", 3));

        var message = Assert.Single(messages.Messages);
        // The element-only Select(e => e.MonsterId) projection reached the lowered terminal's Send target.
        Assert.Equal("monster-7", message.TargetId);
        Assert.Equal("calm", message.Message);
    }

    [Fact]
    public async Task Element_only_runtime_overloads_filter_and_run_without_a_context_parameter()
    {
        // The native (non-lowered) path: the new element-only Where / InvokeLocal overloads forward to
        // the (element, context) forms, so a stage need not take the context it doesn't use.
        var collected = new List<string>();
        using var server = PluginServer.Create(defaultPolicy: ChainPolicy());
        server.Hooks.On<ChainAggroEvent>()
            .Where(e => e.Distance <= 5)
            .InvokeLocal(e => collected.Add(e.MonsterId));

        await server.Hooks.PublishAsync(new ChainAggroEvent("monster-1", 3));
        await server.Hooks.PublishAsync(new ChainAggroEvent("monster-2", 10));

        Assert.Equal(["monster-1"], collected);
    }

    [Fact]
    public async Task Element_only_Select_and_stage_overloads_project_without_a_context_parameter()
    {
        // HookStage element-only Select / Where / InvokeLocal: each stage independently omits the context.
        var collected = new List<int>();
        using var server = PluginServer.Create(defaultPolicy: ChainPolicy());
        server.Hooks.On<ChainAggroEvent>()
            .Select(e => e.Distance)
            .Where(distance => distance <= 5)
            .InvokeLocal(distance => collected.Add(distance));

        await server.Hooks.PublishAsync(new ChainAggroEvent("monster-1", 3));
        await server.Hooks.PublishAsync(new ChainAggroEvent("monster-2", 10));

        Assert.Equal([3], collected);
    }

    [Fact]
    public async Task A_lowered_Where_chain_runs_only_when_its_condition_holds()
    {
        // Interceptors enabled so the generated interceptor file compiles; this test still installs the
        // package directly (UseGeneratedChain) rather than through the interceptor.
        var assembly = Compile(ChainSource, enableInterceptors: true);
        var packageType = assembly.GetTypes().Single(type =>
            type.Name.StartsWith("HookChain_", StringComparison.Ordinal) &&
            type.Name.EndsWith("PluginPackage", StringComparison.Ordinal));
        var package = (PluginPackage)packageType
            .GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, null)!;

        var messages = new InMemoryPluginMessageSink();
        using var server = PluginServer.Create(messages, defaultPolicy: ChainPolicy());
        server.Hooks.On<ChainAggroEvent>().UseGeneratedChain(package);

        await server.Hooks.PublishAsync(new ChainAggroEvent("monster-1", 3));   // 3 <= 5 → fires
        await server.Hooks.PublishAsync(new ChainAggroEvent("monster-2", 10));  // 10 > 5 → skipped

        var message = Assert.Single(messages.Messages);
        Assert.Equal("monster-1", message.TargetId);
        Assert.Equal("calm", message.Message);
    }

    [Fact]
    public async Task The_generated_interceptor_installs_the_chain_at_the_InvokeKernel_call_site()
    {
        // With interceptors enabled, the generated [InterceptsLocation] method replaces the
        // InvokeKernel(lambda) call inside Configure with UseGeneratedChain — so running Configure
        // installs the lowered chain instead of throwing SGP062.
        var assembly = Compile(ChainSource, enableInterceptors: true);

        var messages = new InMemoryPluginMessageSink();
        using var server = PluginServer.Create(messages, defaultPolicy: ChainPolicy());
        var configure = assembly.GetType("ChainSample.Usage")!
            .GetMethod("Configure", BindingFlags.Public | BindingFlags.Static)!;
        configure.Invoke(null, [server.Hooks]);

        await server.Hooks.PublishAsync(new ChainAggroEvent("monster-1", 3));
        await server.Hooks.PublishAsync(new ChainAggroEvent("monster-2", 10));

        var message = Assert.Single(messages.Messages);
        Assert.Equal("monster-1", message.TargetId);
        Assert.Equal("calm", message.Message);
    }

    private static Assembly Compile(string source, bool enableInterceptors)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        if (enableInterceptors)
        {
            parseOptions = parseOptions.WithFeatures(
                [new KeyValuePair<string, string>("InterceptorsNamespaces", "SafeIR.Plugins.Generated")]);
        }

        var compilation = CSharpCompilation.Create(
            "SafeIrChainRuntimeTest",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(ChainAggroEvent).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SafeIrPluginPackageGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));

        using var stream = new MemoryStream();
        var emit = output.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics.Select(d => d.ToString())));
        return Assembly.Load(stream.ToArray());
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
