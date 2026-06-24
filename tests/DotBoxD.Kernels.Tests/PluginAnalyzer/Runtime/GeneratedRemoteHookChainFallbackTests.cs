using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed class GeneratedRemoteHookChainFallbackTests
{
    private static readonly CSharpParseOptions ParseOptions = CSharpParseOptions.Default
        .WithLanguageVersion(LanguageVersion.Preview)
        .WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "DotBoxD.Plugins.Generated")]);

    [Fact]
    public void Same_compilation_generated_registry_aliases_use_the_owning_server_context()
    {
        var result = RunGenerator(GeneratedServerSource);
        var generatedSources = GeneratedSources(result);

        Assert.Contains(generatedSources, source => source.Contains(
            "RemoteHookPipeline<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent, " +
            "global::ChainSample.Plugin.AlphaPluginContext>",
            StringComparison.Ordinal));
        Assert.Contains(generatedSources, source => source.Contains(
            "RemoteHookPipeline<global::ChainSample.Plugin.ChainDamageContext, " +
            "global::ChainSample.Plugin.AlphaPluginContext>",
            StringComparison.Ordinal));
        Assert.Contains(generatedSources, source => source.Contains(
            "RemoteSubscriptionPipeline<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent, " +
            "global::ChainSample.Plugin.BetaPluginContext>",
            StringComparison.Ordinal));
        Assert.Contains(generatedSources, source => source.Contains("UseGeneratedResultChain", StringComparison.Ordinal));
    }

    [Fact]
    public void Prebuilt_sdk_registry_marker_supports_aliases()
    {
        var sdk = CompileReference(PrebuiltSdkSource, "DotBoxDGeneratedRemoteSdk");
        var result = RunGenerator(PrebuiltSdkUsageSource, sdk);

        Assert.Contains(GeneratedSources(result), source => source.Contains(
            "RemoteHookPipeline<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent, " +
            "global::SdkSample.SdkContext>",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Foreign_hooks_named_like_generated_registries_are_not_intercepted()
    {
        var result = RunGenerator(ForeignHookSurfaceSource);

        Assert.DoesNotContain(GeneratedSources(result), source => source.Contains("HookChain_", StringComparison.Ordinal));
        Assert.DoesNotContain(GeneratedSources(result), source =>
            source.Contains("HookChainInterceptors", StringComparison.Ordinal));
    }

    [Fact]
    public void Generated_plugin_server_registries_emit_marker_metadata()
    {
        var result = RunGenerator(GeneratedServerSource);
        var generated = string.Join("\n", GeneratedSources(result));

        Assert.Contains("GeneratedPluginServerRegistryKind.Hook", generated, StringComparison.Ordinal);
        Assert.Contains("GeneratedPluginServerRegistryKind.Subscription", generated, StringComparison.Ordinal);
        Assert.Contains("typeof(global::ChainSample.Plugin.AlphaPluginServer)", generated, StringComparison.Ordinal);
        Assert.Contains("typeof(global::ChainSample.Plugin.AlphaPluginContext)", generated, StringComparison.Ordinal);
    }

    private static GeneratorDriverRunResult RunGenerator(
        string source,
        params MetadataReference[] additionalReferences)
    {
        var compilation = CreateCompilation(source, additionalReferences);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        return driver.GetRunResult();
    }

    private static CSharpCompilation CreateCompilation(
        string source,
        IEnumerable<MetadataReference>? additionalReferences = null)
        => CSharpCompilation.Create(
            "DotBoxDGeneratedRemoteHookFallbackTest",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(DotBoxD.Abstractions.PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(DotBoxD.Services.Peer.RpcPeer).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(
                    typeof(DotBoxD.Services.Attributes.DotBoxDServiceAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(ChainAggroEvent).Assembly.Location))
                .Concat(additionalReferences ?? []),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static MetadataReference CompileReference(string source, string assemblyName)
    {
        var compilation = CreateCompilation(source).WithAssemblyName(assemblyName);
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics.Select(d => d.ToString())));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static string[] GeneratedSources(GeneratorDriverRunResult result)
        => result.GeneratedTrees.Select(tree => tree.GetText().ToString()).ToArray();

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }

    private const string GeneratedServerSource = """
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Services.Attributes;

        namespace ChainSample.Game
        {
            [DotBoxDService]
            public interface IAlphaWorld;

            [DotBoxDService]
            public interface IBetaWorld;
        }

        namespace ChainSample.Game.Ipc
        {
            public readonly record struct LiveSettingUpdate(string Name, string Value);

            public interface IGamePluginControlService : DotBoxD.Plugins.IServerExtensionWireClient
            {
                ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default);
                ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default);
                ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default);
                ValueTask UpdateSettingsAsync(
                    string pluginId,
                    LiveSettingUpdate[] updates,
                    bool atomic = false,
                    CancellationToken ct = default);
                ValueTask HoldUntilShutdownAsync(CancellationToken ct = default);
            }
        }

        namespace DotBoxD.Services.Generated
        {
            public static class DotBoxDGeneratedExtensions
            {
                public static ChainSample.Game.IAlphaWorld GetAlphaWorld(DotBoxD.Services.Peer.RpcPeer peer)
                    => throw new System.InvalidOperationException("not used");

                public static ChainSample.Game.IBetaWorld GetBetaWorld(DotBoxD.Services.Peer.RpcPeer peer)
                    => throw new System.InvalidOperationException("not used");
            }
        }

        namespace ChainSample.Plugin
        {
            [Hook("chain.damage", typeof(ChainDamageResult))]
            public sealed record ChainDamageContext(int Damage);

            [HookResult]
            public readonly partial record struct ChainDamageResult(bool Success, string? Reason, int Damage);

            [GeneratePluginServer]
            public partial class AlphaPluginServer : ChainSample.Game.IAlphaWorld;

            [GeneratePluginServer]
            public partial class BetaPluginServer : ChainSample.Game.IBetaWorld;

            public static class RemoteServerUsage
            {
                public static void Configure(AlphaPluginServer alpha, BetaPluginServer beta)
                {
                    var hooks = alpha.Hooks;
                    hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                        .Where(e => e.Distance <= 5)
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "calm"));

                    hooks.On<ChainDamageContext>()
                        .Where(e => e.Damage > 10)
                        .Register(e => new ChainDamageResult { Success = true, Damage = e.Damage }, 5);

                    beta.Subscriptions.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                        .Where(e => e.Distance <= 5)
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "notify"));
                }
            }
        }
        """;

    private const string PrebuiltSdkSource = """
        using DotBoxD.Abstractions;
        using DotBoxD.Plugins.Runtime;

        namespace SdkSample;

        public sealed class SdkContext
        {
            public SdkContext(HookContext raw) => Raw = raw;
            public HookContext Raw { get; }
            public IPluginMessageSink Messages => Raw.Messages;
            public static SdkContext FromHookContext(HookContext raw) => new(raw);
        }

        public sealed class SdkServer;

        [GeneratedPluginServerRegistry(
            GeneratedPluginServerRegistryKind.Hook,
            typeof(SdkServer),
            typeof(SdkContext))]
        public sealed class SdkHookRegistry
        {
            public RemoteHookPipeline<TEvent, SdkContext> On<TEvent>()
                => throw new System.InvalidOperationException("not used");
        }

        public interface ISdkServer
        {
            SdkHookRegistry Hooks { get; }
        }
        """;

    private const string PrebuiltSdkUsageSource = """
        namespace ChainSample.Plugin;

        public static class RemoteServerUsage
        {
            public static void Configure(global::SdkSample.ISdkServer server)
            {
                var hooks = server.Hooks;
                hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                    .Where(e => e.Distance <= 5)
                    .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "calm"));
            }
        }
        """;

    private const string ForeignHookSurfaceSource = """
        namespace ChainSample.Plugin;

        public sealed class ForeignServer
        {
            public ForeignHookRegistry Hooks { get; } = new();
        }

        public sealed class ForeignHookRegistry
        {
            public ForeignHookPipeline<TEvent> On<TEvent>() => new();
        }

        public sealed class ForeignHookPipeline<TEvent>
        {
            public ForeignHookPipeline<TEvent> Where(global::System.Func<TEvent, bool> predicate) => this;
            public void Run(global::System.Action<TEvent> handler) { }
        }

        public static class RemoteServerUsage
        {
            public static void Direct(ForeignServer server)
            {
                server.Hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                    .Where(e => e.Distance <= 5)
                    .Run(e => { });
            }

            public static void Alias(ForeignServer server)
            {
                var hooks = server.Hooks;
                hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                    .Where(e => e.Distance <= 5)
                    .Run(e => { });
            }
        }
        """;
}
