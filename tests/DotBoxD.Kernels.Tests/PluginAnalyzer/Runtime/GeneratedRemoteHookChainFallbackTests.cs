using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed class GeneratedRemoteHookChainFallbackTests
{
    private const string Source = """
        using DotBoxD.Plugins.Runtime;

        namespace ChainSample;

        public static class RemoteServerUsage
        {
            public static void Configure(IGeneratedWorldServer server)
            {
                server.Hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                    .Where(e => e.Distance <= 5)
                    .Select(e => e.MonsterId)
                    .Run((id, ctx) => ctx.Messages.Send(id, "calm"));
            }
        }
        """;

    private const string SubscriptionSource = """
        using DotBoxD.Plugins.Runtime;

        namespace ChainSample;

        public static class RemoteServerUsage
        {
            public static void Configure(IGeneratedWorldServer server)
            {
                server.Subscriptions.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                    .Where(e => e.Distance <= 5)
                    .Select(e => e.MonsterId)
                    .Run((id, ctx) => ctx.Messages.Send(id, "calm"));
            }
        }
        """;

    private const string ResultSource = """
        using DotBoxD.Abstractions;
        using DotBoxD.Plugins.Runtime;

        namespace ChainSample;

        [Hook("chain.damage", typeof(ChainDamageResult))]
        public sealed record ChainDamageContext(int Damage);

        [HookResult]
        public readonly partial record struct ChainDamageResult(bool Success, string? Reason, int Damage);

        public static class RemoteServerUsage
        {
            public static void Configure(IGeneratedWorldServer server)
            {
                server.Hooks.On<ChainDamageContext>()
                    .Where(e => e.Damage > 10)
                    .Register(e => new ChainDamageResult { Success = true, Damage = e.Damage }, 5);
            }
        }
        """;

    private const string LocalResultSource = """
        using DotBoxD.Abstractions;
        using DotBoxD.Plugins.Runtime;

        namespace ChainSample;

        [Hook("chain.damage", typeof(ChainDamageResult))]
        public sealed record ChainDamageContext(int Damage);

        [HookResult]
        public readonly partial record struct ChainDamageResult(bool Success, string? Reason, int Damage);

        public static class RemoteServerUsage
        {
            public static void Configure(IGeneratedWorldServer server)
            {
                server.Hooks.On<ChainDamageContext>()
                    .Where(e => e.Damage > 10)
                    .RegisterLocal((e, ctx) => new ChainDamageResult { Success = true, Damage = e.Damage }, 5);
            }
        }
        """;

    [Fact]
    public void Fallback_lowers_remote_hook_chains_when_the_server_type_is_generated_later()
        => AssertFallbackLowers(Source, "DotBoxDGeneratedRemoteHookFallbackTest");

    [Fact]
    public void Fallback_lowers_remote_subscription_chains_when_the_server_type_is_generated_later()
        => AssertFallbackLowers(SubscriptionSource, "DotBoxDGeneratedRemoteSubscriptionFallbackTest");

    [Fact]
    public void Fallback_lowers_remote_Register_result_chains_when_the_server_type_is_generated_later()
        => AssertFallbackLowers(
            ResultSource,
            "DotBoxDGeneratedRemoteResultFallbackTest",
            "UseGeneratedResultChain",
            "RemoteHookPipeline<global::ChainSample.ChainDamageContext>");

    [Fact]
    public void Fallback_lowers_remote_RegisterLocal_result_chains_when_the_server_type_is_generated_later()
        => AssertFallbackLowers(
            LocalResultSource,
            "DotBoxDGeneratedRemoteLocalResultFallbackTest",
            "UseGeneratedLocalResultChain",
            "RemoteHookPipeline<global::ChainSample.ChainDamageContext>");

    private static void AssertFallbackLowers(
        string source,
        string assemblyName,
        string? expectedInstall = null,
        string? expectedReceiver = null)
    {
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview)
            .WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "DotBoxD.Plugins.Generated")]);
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(ChainAggroEvent).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);

        driver = driver.RunGenerators(compilation);

        var result = driver.GetRunResult();
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedSources = result.GeneratedTrees.Select(tree => tree.GetText().ToString()).ToArray();
        Assert.Contains(generatedSources, source => source.Contains("HookChain_", StringComparison.Ordinal));
        Assert.Contains(generatedSources, source => source.Contains("HookChainInterceptors", StringComparison.Ordinal));
        if (expectedInstall is not null)
        {
            Assert.Contains(generatedSources, source => source.Contains(expectedInstall, StringComparison.Ordinal));
        }

        if (expectedReceiver is not null)
        {
            var interceptorSource = Assert.Single(
                generatedSources,
                source => source.Contains("HookChainInterceptors", StringComparison.Ordinal));
            Assert.Contains(expectedReceiver, interceptorSource, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "RemoteHookStage<global::ChainSample.ChainDamageContext",
                interceptorSource,
                StringComparison.Ordinal);
        }
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
