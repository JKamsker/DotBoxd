using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Plugins.Analyzer.Analysis.HookChains;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
    public void Same_compilation_generated_server_fields_and_properties_use_the_owning_server_context()
    {
        var result = RunGenerator(GeneratedServerSource + """

            namespace ChainSample.Plugin
            {
            public sealed class FieldUsage
            {
                private AlphaPluginServer _server = null!;

                public void Configure()
                    => this._server.Hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                        .Where(e => e.Distance <= 5)
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "field"));
            }

            public sealed class PropertyUsage
            {
                public AlphaPluginServer Server { get; init; } = null!;

                public void Configure()
                    => this.Server.Hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                        .Where(e => e.Distance <= 5)
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "property"));
            }
            }
            """);
        var generated = string.Join("\n", GeneratedSources(result));
        const string Pipeline = "RemoteHookPipeline<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent, " +
                                "global::ChainSample.Plugin.AlphaPluginContext>";

        Assert.True(Count(generated, Pipeline) >= 3, generated);
    }

    [Fact]
    public void Same_compilation_generated_registry_type_supports_using_alias()
    {
        var result = RunGenerator(GeneratedServerSource + """

            namespace ChainSample.Plugin
            {
            using AlphaHooks = ChainSample.Plugin.AlphaPluginHookRegistry;

            public sealed class AliasTypedUsage
            {
                private readonly AlphaHooks _hooks;

                public AliasTypedUsage(AlphaPluginServer server)
                    => _hooks = server.Hooks;

                public void Configure()
                    => _hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                        .Where(e => e.Distance <= 5)
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "field-alias"));
            }
            }
            """);
        var generated = string.Join("\n", GeneratedSources(result));
        const string Pipeline = "RemoteHookPipeline<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent, " +
                                "global::ChainSample.Plugin.AlphaPluginContext>";

        Assert.Contains("field-alias", generated, StringComparison.Ordinal);
        Assert.Contains(Pipeline, generated, StringComparison.Ordinal);
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

    [Fact]
    public void Registry_type_declared_in_another_syntax_tree_does_not_throw()
    {
        var declarationTree = CSharpSyntaxTree.ParseText("""
            namespace ChainSample.Plugin;

            public sealed partial class Owner
            {
                public ForeignHookRegistry Hooks { get; } = new();
            }

            public sealed class ForeignHookRegistry
            {
                public ForeignHookPipeline<TEvent> On<TEvent>() => new();
            }

            public sealed class ForeignHookPipeline<TEvent>
            {
                public void Run(global::System.Action<TEvent> handler) { }
            }
            """, ParseOptions);
        var usageTree = CSharpSyntaxTree.ParseText("""
            namespace ChainSample.Plugin;

            public sealed partial class Owner
            {
                public void Configure()
                    => Hooks.On<int>().Run(_ => { });
            }
            """, ParseOptions);
        var compilation = CSharpCompilation.Create(
            "DotBoxDGeneratedRemoteHookFallbackCrossTreeTest",
            [declarationTree, usageTree],
            TrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var onInvocation = usageTree.GetRoot().DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(static invocation => invocation.Expression is MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "On"
            });

        var candidate = GeneratedRemoteHookChainFallback.Candidate(
            onInvocation,
            compilation.GetSemanticModel(usageTree),
            CancellationToken.None);

        Assert.Null(candidate);
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

    private static int Count(string value, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }

}
