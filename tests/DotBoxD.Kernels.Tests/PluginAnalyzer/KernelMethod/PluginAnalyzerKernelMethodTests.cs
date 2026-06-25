using System.Reflection;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Plugins.Policies;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod;

/// <summary>An event a generated kernel-method hook chain subscribes to (referenced from chain source).</summary>
public sealed record KernelMethodAggroEvent(string MonsterId, int Distance, int MonsterLevel, int PlayerLevel);

/// <summary>
/// Coverage of <c>[KernelMethod]</c> inlining (Followup #1): a static helper carrying
/// <c>[KernelMethod]</c> is inlined into the calling kernel/hook IR exactly as if its body were written
/// at the call site — its parameters are replaced by the already-lowered argument IR. Proven for a
/// kernel-class <c>ShouldHandle</c>, for an inline <c>Where</c> hook chain, for multi-argument helpers,
/// and for capability collection when an argument is a <c>[HostBinding]</c> call. Unsupported shapes
/// (multi-statement body, non-static) fail safe (no package; the runtime terminal throws DBXK062).
/// </summary>
public sealed class PluginAnalyzerKernelMethodTests
{
    [Fact]
    public async Task KernelMethod_inlined_into_ShouldHandle_gates_like_the_inline_expression()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            PluginAnalyzerKernelMethodTestSources.InlinedGate,
            "Sample.InlinedGatePluginPackage");
        using var server = DotBoxD.Plugins.PluginServer.Create(new InMemoryPluginMessageSink(), defaultPolicy: SandboxedPolicy());
        var kernel = await server.InstallAsync(package);
        var adapter = new AggroAdapter();

        // 10-5=5 >= 3 (bullying) AND 3 <= 5 (close) → handled.
        Assert.True((bool)await kernel.ShouldHandleAsync(adapter, new AggroSample("m", "calm", 10, 5, 3)));
        // 6-5=1 >= 3 is false → not handled (first inlined method short-circuits).
        Assert.False((bool)await kernel.ShouldHandleAsync(adapter, new AggroSample("m", "calm", 6, 5, 3)));
        // bullying but 9 <= 5 is false → not handled (second inlined method).
        Assert.False((bool)await kernel.ShouldHandleAsync(adapter, new AggroSample("m", "calm", 10, 5, 9)));
    }

    [Fact]
    public void KernelMethod_collects_capabilities_of_a_host_binding_argument()
    {
        // The host-binding call is an argument to the inlined [KernelMethod]; it lowers in the call-site
        // context so its capability still lands in the manifest.
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            PluginAnalyzerKernelMethodTestSources.InlinedHostBinding,
            "Sample.InlinedHostBindingPluginPackage");

        Assert.Contains("probe.read.value", package.Manifest.RequiredCapabilities);
        Assert.Contains("host.message.write", package.Manifest.RequiredCapabilities);
    }

    [Fact]
    public async Task KernelMethod_with_a_host_binding_argument_installs_and_runs_under_a_wildcard_grant()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            PluginAnalyzerKernelMethodTestSources.InlinedHostBinding,
            "Sample.InlinedHostBindingPluginPackage");
        using var server = DotBoxD.Plugins.PluginServer.Create(
            new InMemoryPluginMessageSink(),
            configureHost: AddProbeBindings,
            defaultPolicy: ProbeReadPolicy());

        var kernel = await server.InstallAsync(package);
        var adapter = new ProbeAdapter();

        // host.probe.getValue returns 42.
        Assert.True((bool)await kernel.ShouldHandleAsync(adapter, new ProbeSample("p", "hi", 10)));
        Assert.False((bool)await kernel.ShouldHandleAsync(adapter, new ProbeSample("p", "hi", 50)));
    }

    [Fact]
    public async Task KernelMethod_inlined_into_an_inline_Where_chain_runs_only_when_its_condition_holds()
    {
        var assembly = Compile(PluginAnalyzerKernelMethodTestSources.Chain, enableInterceptors: true);
        var package = HookChainPackage(assembly);

        var messages = new InMemoryPluginMessageSink();
        using var server = DotBoxD.Plugins.PluginServer.Create(messages, defaultPolicy: SandboxedPolicy());
        server.Hooks.On<KernelMethodAggroEvent>().UseGeneratedChain(package);

        // bullying (10-5>=3) AND close (3<=5) → fires.
        await server.Hooks.PublishAsync(new KernelMethodAggroEvent("monster-1", 3, 10, 5));
        // not bullying (6-5<3) → skipped.
        await server.Hooks.PublishAsync(new KernelMethodAggroEvent("monster-2", 3, 6, 5));
        // bullying but far (9>5) → skipped.
        await server.Hooks.PublishAsync(new KernelMethodAggroEvent("monster-3", 9, 10, 5));

        var message = Assert.Single(messages.Messages);
        Assert.Equal("monster-1", message.TargetId);
        Assert.Equal("calm", message.Message);
    }

    [Fact]
    public async Task KernelMethod_accepts_record_projection_parameters_in_inline_chains()
    {
        var assembly = Compile(PluginAnalyzerKernelMethodTestSources.RichRecordHelperChain, enableInterceptors: true);
        var package = HookChainPackage(assembly);

        var messages = new InMemoryPluginMessageSink();
        using var server = DotBoxD.Plugins.PluginServer.Create(messages, defaultPolicy: SandboxedPolicy());
        server.Hooks.On<KernelMethodAggroEvent>().UseGeneratedChain(package);

        await server.Hooks.PublishAsync(new KernelMethodAggroEvent("monster-1", 3, 10, 5));
        await server.Hooks.PublishAsync(new KernelMethodAggroEvent("monster-2", 9, 10, 5));

        var message = Assert.Single(messages.Messages);
        Assert.Equal("monster-1", message.TargetId);
        Assert.Equal("calm", message.Message);
    }

    [Fact]
    public async Task KernelMethod_send_helper_inlined_into_Run_terminal_sends_message()
    {
        var assembly = Compile(PluginAnalyzerKernelMethodTestSources.RunSendHelperChain, enableInterceptors: true);
        var package = HookChainPackage(assembly);

        var messages = new InMemoryPluginMessageSink();
        using var server = DotBoxD.Plugins.PluginServer.Create(messages, defaultPolicy: SandboxedPolicy());
        server.Hooks.On<KernelMethodAggroEvent>().UseGeneratedChain(package);

        await server.Hooks.PublishAsync(new KernelMethodAggroEvent("monster-1", 3, 10, 5));
        await server.Hooks.PublishAsync(new KernelMethodAggroEvent("monster-2", 9, 10, 5));

        var message = Assert.Single(messages.Messages);
        Assert.Equal("monster-1", message.TargetId);
        Assert.Equal("calm", message.Message);
    }

    [Fact]
    public async Task KernelMethod_send_helper_inlined_into_kernel_Handle_sends_message()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            PluginAnalyzerKernelMethodTestSources.HandleSendHelper,
            "Sample.InlinedHandleHelperPluginPackage");
        var messages = new InMemoryPluginMessageSink();
        using var server = DotBoxD.Plugins.PluginServer.Create(messages, defaultPolicy: SandboxedPolicy());

        var kernel = await server.InstallAsync(package);
        await kernel.HandleAsync(new AggroAdapter(), new AggroSample("monster-1", "ignored", 10, 5, 3));

        var message = Assert.Single(messages.Messages);
        Assert.Equal("monster-1", message.TargetId);
        Assert.Equal("calm", message.Message);
    }

    [Fact]
    public void A_KernelMethod_with_a_multi_statement_body_fails_safe_with_no_generated_chain_package()
    {
        var assembly = Compile(PluginAnalyzerKernelMethodTestSources.MultiStatement, enableInterceptors: true);

        var hasChainPackage = assembly.GetTypes().Any(type =>
            type.Name.StartsWith("HookChain_", StringComparison.Ordinal) &&
            type.Name.EndsWith("PluginPackage", StringComparison.Ordinal));

        Assert.False(hasChainPackage);
    }

    private static PluginPackage HookChainPackage(Assembly assembly)
    {
        var packageType = assembly.GetTypes().Single(type =>
            type.Name.StartsWith("HookChain_", StringComparison.Ordinal) &&
            type.Name.EndsWith("PluginPackage", StringComparison.Ordinal));
        return (PluginPackage)packageType
            .GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, null)!;
    }

    private static Assembly Compile(string source, bool enableInterceptors)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        if (enableInterceptors)
        {
            parseOptions = parseOptions.WithFeatures(
                [new KeyValuePair<string, string>("InterceptorsNamespaces", "DotBoxD.Plugins.Generated")]);
        }

        var compilation = CSharpCompilation.Create(
            "DotBoxDKernelMethodTest",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(KernelMethodAggroEvent).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));

        using var stream = new MemoryStream();
        var emit = output.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics.Select(d => d.ToString())));
        return Assembly.Load(stream.ToArray());
    }

    private static SandboxPolicy SandboxedPolicy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .GrantHostMessageWrite()
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();

    private static SandboxPolicy ProbeReadPolicy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .GrantHostMessageWrite()
            .Grant("probe.read.*", new { }, SandboxEffect.HostStateRead)
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();

    private static void AddProbeBindings(SandboxHostBuilder builder)
        => builder.AddBinding(new BindingDescriptor(
            "host.probe.getValue",
            SemVersion.One,
            [SandboxType.String],
            SandboxType.I32,
            SandboxEffect.Cpu | SandboxEffect.HostStateRead,
            "probe.read.value",
            BindingCostModel.Fixed(2),
            AuditLevel.PerResource,
            BindingSafety.ReadOnlyExternal,
            (context, args, _) =>
            {
                var startedAt = DateTimeOffset.UtcNow;
                var entityId = ((StringValue)args[0]).Value;
                context.Audit.Write(new SandboxAuditEvent(
                    context.RunId,
                    "BindingCall",
                    startedAt,
                    true,
                    BindingId: "host.probe.getValue",
                    CapabilityId: "probe.read.value",
                    Effect: SandboxEffect.HostStateRead,
                    ResourceId: $"entity:{entityId}",
                    Fields: context.BindingAuditFields("probe", startedAt)));
                return ValueTask.FromResult(SandboxValue.FromInt32(42));
            },
            CompiledBinding.RuntimeStub("DotBoxD.Kernels.Runtime.CompiledRuntime", "CallBinding"),
            GrantValidator: static (_, _) => { }));

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }

}
