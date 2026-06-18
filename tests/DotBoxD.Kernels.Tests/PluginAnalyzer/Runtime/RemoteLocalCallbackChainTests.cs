using System.Reflection;
using DotBoxD.Abstractions;
using DotBoxD.Kernels;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Plugins.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed class RemoteLocalCallbackChainTests
{
    private const string Source = """
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using DotBoxD.Plugins.Runtime;

        namespace ChainSample;

        public static class RemoteUsage
        {
            public static readonly List<string> Seen = new();

            public static void Configure(RemoteSubscriptionRegistry subscriptions)
                => subscriptions.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                    .Where(e => e.Distance <= 5)
                    .RunLocal((e, ctx) =>
                    {
                        Seen.Add(e.MonsterId);
                        return ValueTask.CompletedTask;
                    });
        }
        """;

    private const string ProjectedSource = """
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using DotBoxD.Plugins.Runtime;

        namespace ChainSample;

        public static class RemoteUsage
        {
            public static readonly List<string> Seen = new();

            public static void Configure(RemoteSubscriptionRegistry subscriptions)
                => subscriptions.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                    .Where(e => e.Distance <= 5)
                    .Select(e => e.MonsterId)
                    .RunLocal((id, ctx) =>
                    {
                        Seen.Add(id);
                        return ValueTask.CompletedTask;
                    });
        }
        """;

    [Fact]
    public async Task Remote_RunLocal_installs_an_indexed_filter_package_and_keeps_the_local_callback()
    {
        var assembly = Compile(Source, enableInterceptors: true);
        var installed = new List<PluginPackage>();
        var callbacks = new List<RemoteLocalCallbackRegistration>();
        var registry = new RemoteSubscriptionRegistry(
            package =>
            {
                installed.Add(package);
                return ValueTask.FromResult(package.Manifest.PluginId);
            },
            registration =>
            {
                callbacks.Add(registration);
                return ValueTask.FromResult(registration.Package.Manifest.PluginId);
            });

        Configure(assembly, registry);

        Assert.Empty(installed);
        var callback = Assert.Single(callbacks);
        Assert.Equal(typeof(ChainAggroEvent), callback.EventType);
        Assert.Equal(RemoteLocalCallbackPayloadKind.Event, callback.Payload.Kind);
        Assert.Equal(typeof(ChainAggroEvent), callback.Payload.Type);
        Assert.Null(callback.Payload.Entrypoint);
        var subscription = Assert.Single(callback.Package.Manifest.Subscriptions);
        Assert.True(subscription.IndexCoversPredicate);
        var predicate = Assert.Single(subscription.IndexedPredicates);
        Assert.Equal("Distance", predicate.Path);
        Assert.Equal(IndexPredicateOperator.LessThanOrEqual, predicate.Operator);
        Assert.Equal(5, Assert.IsType<int>(predicate.Value));
        Assert.DoesNotContain(PluginMessageBindings.CapabilityId, callback.Package.Manifest.RequiredCapabilities);

        var handler = Assert.IsType<Func<ChainAggroEvent, HookContext, ValueTask>>(callback.Handler);
        await handler(
            new ChainAggroEvent("monster-1", 3),
            new HookContext(new InMemoryPluginMessageSink(), CancellationToken.None));

        Assert.Equal(["monster-1"], Seen(assembly));
    }

    [Fact]
    public async Task Remote_RunLocal_with_Select_installs_a_server_projection_payload()
    {
        var assembly = Compile(ProjectedSource, enableInterceptors: true);
        var callbacks = new List<RemoteLocalCallbackRegistration>();
        var registry = new RemoteSubscriptionRegistry(
            package => ValueTask.FromResult(package.Manifest.PluginId),
            registration =>
            {
                callbacks.Add(registration);
                return ValueTask.FromResult(registration.Package.Manifest.PluginId);
            });

        Configure(assembly, registry);

        var callback = Assert.Single(callbacks);
        Assert.Equal(typeof(ChainAggroEvent), callback.EventType);
        Assert.Equal(RemoteLocalCallbackPayloadKind.Projection, callback.Payload.Kind);
        Assert.Equal(typeof(string), callback.Payload.Type);
        Assert.Equal(callback.Package.Entrypoints.Handle, callback.Payload.Entrypoint);
        var handle = callback.Package.Module.Functions.Single(
            function => string.Equals(function.Id, callback.Package.Entrypoints.Handle, StringComparison.Ordinal));
        Assert.Equal(SandboxType.String, handle.ReturnType);
        Assert.IsType<AssignmentStatement>(Assert.Single(handle.Body.SkipLast(1)));
        Assert.IsType<ReturnStatement>(handle.Body[^1]);

        using var server = DotBoxD.Plugins.PluginServer.Create();
        var kernel = await server.InstallLocalCallbackAsync(callback.Package);
        var adapter = server.Events.Resolve<ChainAggroEvent>();
        var acceptedPayload = await kernel.TryEvaluateHandleAsync(adapter, new ChainAggroEvent("monster-1", 3));
        Assert.Equal("monster-1", Assert.IsType<StringValue>(acceptedPayload).Value);
        var rejectedPayload = await kernel.TryEvaluateHandleAsync(adapter, new ChainAggroEvent("monster-2", 10));
        Assert.Null(rejectedPayload);

        var handler = Assert.IsType<Func<string, HookContext, ValueTask>>(callback.Handler);
        await handler(
            "monster-1",
            new HookContext(new InMemoryPluginMessageSink(), CancellationToken.None));

        Assert.Equal(["monster-1"], Seen(assembly));
    }

    private static void Configure(Assembly assembly, RemoteSubscriptionRegistry registry)
        => assembly.GetType("ChainSample.RemoteUsage")!
            .GetMethod("Configure", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [registry]);

    private static IReadOnlyList<string> Seen(Assembly assembly)
        => (IReadOnlyList<string>)assembly.GetType("ChainSample.RemoteUsage")!
            .GetField("Seen", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;

    private static Assembly Compile(string source, bool enableInterceptors)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        if (enableInterceptors)
        {
            parseOptions = parseOptions.WithFeatures(
                [new KeyValuePair<string, string>("InterceptorsNamespaces", "DotBoxD.Plugins.Generated")]);
        }

        var compilation = CSharpCompilation.Create(
            "DotBoxDRemoteLocalCallbackChainTest",
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
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        using var stream = new MemoryStream();
        var emit = output.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics.Select(d => d.ToString())));
        return Assembly.Load(stream.ToArray());
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
