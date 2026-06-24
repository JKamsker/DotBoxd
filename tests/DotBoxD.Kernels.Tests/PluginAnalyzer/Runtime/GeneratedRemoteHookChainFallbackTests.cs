using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed partial class GeneratedRemoteHookChainFallbackTests
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
    public void Same_simple_name_foreign_registry_alias_is_not_intercepted()
    {
        var result = RunGenerator(SameSimpleNameForeignRegistrySource);

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

    [Fact]
    public void Throw_only_RunLocal_block_uses_converted_non_void_handler_shape()
    {
        var result = RunGenerator(GeneratedServerSource + """

            namespace ChainSample.Plugin
            {
            public static class ThrowOnlyUsage
            {
                public static void Configure(AlphaPluginServer server)
                    => server.Hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                        .RunLocal(global::System.Threading.Tasks.ValueTask (e, ctx) =>
                        {
                            throw new global::System.InvalidOperationException("boom");
                        });
            }
            }
            """);
        var generated = string.Join("\n", GeneratedSources(result));

        Assert.Contains(
            "global::System.Func<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent, " +
            "global::ChainSample.Plugin.AlphaPluginContext, global::System.Threading.Tasks.ValueTask>",
            generated,
            StringComparison.Ordinal);
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

}
