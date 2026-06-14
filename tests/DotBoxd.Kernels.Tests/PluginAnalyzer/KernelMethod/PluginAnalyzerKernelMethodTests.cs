using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using DotBoxd.Kernels;
using DotBoxd.Hosting;
using DotBoxd.Plugins.Analyzer;
using DotBoxd.Plugins;

namespace DotBoxd.Kernels.Tests;

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
    private const string InlinedGateSource = """
        using DotBoxd.Plugins;
        using DotBoxd.Abstractions;

        namespace Sample;

        public sealed record AggroEvent(
            string MonsterId, string Message, int MonsterLevel, int PlayerLevel, int Distance);

        [Plugin("inlined-gate")]
        public sealed partial class InlinedGateKernel : IEventKernel<AggroEvent>
        {
            public bool ShouldHandle(AggroEvent e, HookContext ctx)
                => IsBullying(e.MonsterLevel, e.PlayerLevel) && IsClose(e.Distance);

            public void Handle(AggroEvent e, HookContext ctx)
                => ctx.Messages.Send(e.MonsterId, e.Message);

            [KernelMethod]
            public static bool IsBullying(int monsterLevel, int playerLevel) => monsterLevel - playerLevel >= 3;

            [KernelMethod]
            public static bool IsClose(int distance) => distance <= 5;
        }
        """;

    [Fact]
    public async Task KernelMethod_inlined_into_ShouldHandle_gates_like_the_inline_expression()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(InlinedGateSource, "Sample.InlinedGatePluginPackage");
        using var server = PluginServer.Create(new InMemoryPluginMessageSink(), defaultPolicy: SandboxedPolicy());
        var kernel = await server.InstallAsync(package);
        var adapter = new AggroAdapter();

        // 10-5=5 >= 3 (bullying) AND 3 <= 5 (close) → handled.
        Assert.True(await kernel.ShouldHandleAsync(adapter, new AggroSample("m", "calm", 10, 5, 3)));
        // 6-5=1 >= 3 is false → not handled (first inlined method short-circuits).
        Assert.False(await kernel.ShouldHandleAsync(adapter, new AggroSample("m", "calm", 6, 5, 3)));
        // bullying but 9 <= 5 is false → not handled (second inlined method).
        Assert.False(await kernel.ShouldHandleAsync(adapter, new AggroSample("m", "calm", 10, 5, 9)));
    }

    private const string InlinedHostBindingSource = """
        using DotBoxd.Kernels;
        using DotBoxd.Plugins;
        using DotBoxd.Abstractions;

        namespace Sample;

        public interface IProbeWorld
        {
            [HostBinding("host.probe.getValue", "probe.read.value", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
            int GetValue(string id);
        }

        public sealed record ProbeEvent(string TargetId, string Message, int Threshold);

        [Plugin("inlined-host-binding")]
        public sealed partial class InlinedHostBindingKernel : IEventKernel<ProbeEvent>
        {
            public bool ShouldHandle(ProbeEvent e, HookContext ctx)
                => IsAtLeast(ctx.Host<IProbeWorld>().GetValue(e.TargetId), e.Threshold);

            public void Handle(ProbeEvent e, HookContext ctx)
                => ctx.Messages.Send(e.TargetId, e.Message);

            [KernelMethod]
            public static bool IsAtLeast(int value, int threshold) => value >= threshold;
        }
        """;

    [Fact]
    public void KernelMethod_collects_capabilities_of_a_host_binding_argument()
    {
        // The host-binding call is an argument to the inlined [KernelMethod]; it lowers in the call-site
        // context so its capability still lands in the manifest.
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            InlinedHostBindingSource, "Sample.InlinedHostBindingPluginPackage");

        Assert.Contains("probe.read.value", package.Manifest.RequiredCapabilities);
        Assert.Contains("host.message.write", package.Manifest.RequiredCapabilities);
    }

    [Fact]
    public async Task KernelMethod_with_a_host_binding_argument_installs_and_runs_under_a_wildcard_grant()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            InlinedHostBindingSource, "Sample.InlinedHostBindingPluginPackage");
        using var server = PluginServer.Create(
            new InMemoryPluginMessageSink(),
            configureHost: AddProbeBindings,
            defaultPolicy: ProbeReadPolicy());

        var kernel = await server.InstallAsync(package);
        var adapter = new ProbeAdapter();

        // host.probe.getValue returns 42.
        Assert.True(await kernel.ShouldHandleAsync(adapter, new ProbeSample("p", "hi", 10)));
        Assert.False(await kernel.ShouldHandleAsync(adapter, new ProbeSample("p", "hi", 50)));
    }

    private const string ChainSource = """
        using DotBoxd.Plugins;
        using DotBoxd.Abstractions;

        namespace ChainSample;

        public static class Usage
        {
            public static void Configure(HookRegistry hooks)
                => hooks.On<global::DotBoxd.Kernels.Tests.KernelMethodAggroEvent>()
                    .Where((e, ctx) => IsBullyingAndClose(e.MonsterLevel, e.PlayerLevel, e.Distance))
                    .InvokeKernel((e, ctx) => ctx.Messages.Send(e.MonsterId, "calm"));

            [KernelMethod]
            public static bool IsBullyingAndClose(int monsterLevel, int playerLevel, int distance)
                => monsterLevel - playerLevel >= 3 && distance <= 5;
        }
        """;

    [Fact]
    public async Task KernelMethod_inlined_into_an_inline_Where_chain_runs_only_when_its_condition_holds()
    {
        var assembly = Compile(ChainSource, enableInterceptors: true);
        var package = HookChainPackage(assembly);

        var messages = new InMemoryPluginMessageSink();
        using var server = PluginServer.Create(messages, defaultPolicy: SandboxedPolicy());
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

    private const string MultiStatementSource = """
        using DotBoxd.Plugins;
        using DotBoxd.Abstractions;

        namespace ChainSample;

        public static class Usage
        {
            public static void Configure(HookRegistry hooks)
                => hooks.On<global::DotBoxd.Kernels.Tests.KernelMethodAggroEvent>()
                    .Where((e, ctx) => Unsupported(e.Distance))
                    .InvokeKernel((e, ctx) => ctx.Messages.Send(e.MonsterId, "calm"));

            // A multi-statement body is not inlineable → the whole chain fails safe (no package).
            [KernelMethod]
            public static bool Unsupported(int distance)
            {
                var doubled = distance * 2;
                return doubled <= 10;
            }
        }
        """;

    [Fact]
    public void A_KernelMethod_with_a_multi_statement_body_fails_safe_with_no_generated_chain_package()
    {
        var assembly = Compile(MultiStatementSource, enableInterceptors: true);

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
                [new KeyValuePair<string, string>("InterceptorsNamespaces", "DotBoxd.Plugins.Generated")]);
        }

        var compilation = CSharpCompilation.Create(
            "DotBoxdKernelMethodTest",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(KernelMethodAggroEvent).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new DotBoxdPluginPackageGenerator().AsSourceGenerator()],
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
            CompiledBinding.RuntimeStub("DotBoxd.Kernels.Runtime.CompiledRuntime", "CallBinding"),
            GrantValidator: static (_, _) => { }));

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }

    private sealed record AggroSample(
        string MonsterId, string Message, int MonsterLevel, int PlayerLevel, int Distance);

    private sealed class AggroAdapter : IPluginEventAdapter<AggroSample>
    {
        public string EventName => "AggroEvent";

        public IReadOnlyList<Parameter> Parameters { get; } =
        [
            new("e_MonsterId", SandboxType.String),
            new("e_Message", SandboxType.String),
            new("e_MonsterLevel", SandboxType.I32),
            new("e_PlayerLevel", SandboxType.I32),
            new("e_Distance", SandboxType.I32)
        ];

        public IReadOnlyList<SandboxValue> ToSandboxValues(AggroSample e)
            =>
            [
                SandboxValue.FromString(e.MonsterId),
                SandboxValue.FromString(e.Message),
                SandboxValue.FromInt32(e.MonsterLevel),
                SandboxValue.FromInt32(e.PlayerLevel),
                SandboxValue.FromInt32(e.Distance)
            ];
    }

    private sealed record ProbeSample(string TargetId, string Message, int Threshold);

    private sealed class ProbeAdapter : IPluginEventAdapter<ProbeSample>
    {
        public string EventName => "ProbeEvent";

        public IReadOnlyList<Parameter> Parameters { get; } =
        [
            new("e_TargetId", SandboxType.String),
            new("e_Message", SandboxType.String),
            new("e_Threshold", SandboxType.I32)
        ];

        public IReadOnlyList<SandboxValue> ToSandboxValues(ProbeSample e)
            =>
            [
                SandboxValue.FromString(e.TargetId),
                SandboxValue.FromString(e.Message),
                SandboxValue.FromInt32(e.Threshold)
            ];
    }
}
