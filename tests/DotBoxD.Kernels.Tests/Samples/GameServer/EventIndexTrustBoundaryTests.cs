extern alias GameServerAbstractions;

using System.Collections.Concurrent;
using System.Reflection;
using DotBoxD.Kernels.Policies;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Plugins.Indexing;
using DotBoxD.Plugins.Json;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Policies;
using GameServerAbstractions::DotBoxD.Kernels.Game.Server.Abstractions.Events;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.Samples.GameServer;

public sealed class EventIndexTrustBoundaryTests
{
    [Fact]
    public async Task Direct_package_index_coverage_tamper_still_runs_verified_predicate()
    {
        var package = TamperIndexCoverage(GeneratedAttackPackage());

        await AssertIndexSurvivorStillRunsShouldHandleAsync(package);
    }

    [Fact]
    public async Task Json_imported_index_coverage_tamper_still_runs_verified_predicate()
    {
        var package = PluginPackageJsonSerializer.Import(
            PluginPackageJsonSerializer.Export(TamperIndexCoverage(GeneratedAttackPackage())));

        await AssertIndexSurvivorStillRunsShouldHandleAsync(package);
    }

    [Fact]
    public async Task Direct_package_index_predicate_tamper_cannot_prefilter_verified_match()
        => await AssertTamperedIndexPredicatesCannotDropVerifiedMatchAsync(
            TamperIndexPredicates(GeneratedAttackPackage()));

    [Fact]
    public async Task Json_imported_index_predicate_tamper_cannot_prefilter_verified_match()
        => await AssertTamperedIndexPredicatesCannotDropVerifiedMatchAsync(
            PluginPackageJsonSerializer.Import(
                PluginPackageJsonSerializer.Export(TamperIndexPredicates(GeneratedAttackPackage()))));

    [Fact]
    public async Task WireSubscription_recomputes_index_route_when_manifest_predicates_are_missing()
    {
        var sink = new RecordingMessageSink();
        using var server = PluginServer.Create(sink, defaultPolicy: ChainPolicy());
        _ = server.Events.Resolve<AttackEvent>();
        var kernel = await server.InstallAsync(RemoveIndexPredicates(GeneratedAttackPackage()), ChainPolicy());
        var registry = new EventIndexRegistry();

        server.WireSubscription(kernel, new WireOptions { IndexRegistry = registry });
        registry.Publish(new AttackEvent("player-1", "player-2", Damage: 7, AttackerLevel: 8));

        await registry.DrainAsync();
        Assert.Equal(1, registry.Stats.Considered);
        Assert.Equal(1, registry.Stats.Dispatched);
        var message = Assert.Single(sink.Messages);
        Assert.Equal("player-2", message.TargetId);
        Assert.Equal("indexed-taunt:inline", message.Message);
    }

    [Fact]
    public async Task Session_dispose_unregisters_indexed_subscription()
    {
        var package = GeneratedAttackPackage();
        using var server = PluginServer.Create(new RecordingMessageSink(), defaultPolicy: ChainPolicy());
        using var session = server.CreateSession();
        var kernel = await session.InstallAsync(package, ChainPolicy());

        var registry = RegisterAttackIndex(server, kernel);
        session.Dispose();
        registry.Publish(new AttackEvent("player-1", "player-2", Damage: 7, AttackerLevel: 8));

        await registry.DrainAsync();
        Assert.Equal(0, registry.Stats.Considered);
    }

    [Fact]
    public async Task Hot_replace_unregisters_old_indexed_subscription()
    {
        var package = GeneratedAttackPackage();
        using var server = PluginServer.Create(new RecordingMessageSink(), defaultPolicy: ChainPolicy());
        var first = await server.InstallAsync(package, ChainPolicy());

        var registry = RegisterAttackIndex(server, first);
        await server.InstallAsync(package, ChainPolicy());
        registry.Publish(new AttackEvent("player-1", "player-2", Damage: 7, AttackerLevel: 8));

        await registry.DrainAsync();
        Assert.Equal(0, registry.Stats.Considered);
    }

    private static async Task AssertIndexSurvivorStillRunsShouldHandleAsync(PluginPackage package)
    {
        var sink = new RecordingMessageSink();
        using var server = PluginServer.Create(sink, defaultPolicy: ChainPolicy());
        var kernel = await server.InstallAsync(package, ChainPolicy());

        var registry = RegisterAttackIndex(server, kernel);
        registry.Publish(new AttackEvent("player-1", "player-2", Damage: 1, AttackerLevel: 8));

        await registry.DrainAsync();
        Assert.Equal(1, registry.Stats.Considered);
        Assert.Equal(1, registry.Stats.Prefiltered);
        Assert.Equal(0, registry.Stats.Dispatched);
        Assert.Empty(sink.Messages);
    }

    private static async Task AssertTamperedIndexPredicatesCannotDropVerifiedMatchAsync(PluginPackage package)
    {
        var sink = new RecordingMessageSink();
        using var server = PluginServer.Create(sink, defaultPolicy: ChainPolicy());
        var kernel = await server.InstallAsync(package, ChainPolicy());

        var registry = RegisterAttackIndex(server, kernel);
        registry.Publish(new AttackEvent("player-1", "player-2", Damage: 7, AttackerLevel: 8));

        await registry.DrainAsync();
        Assert.Equal(1, registry.Stats.Considered);
        Assert.Equal(1, registry.Stats.Dispatched);
        Assert.Equal(0, registry.Stats.Prefiltered);
        var message = Assert.Single(sink.Messages);
        Assert.Equal("player-2", message.TargetId);
        Assert.Equal("indexed-taunt:inline", message.Message);
    }

    private static EventIndexRegistry RegisterAttackIndex(PluginServer server, InstalledKernel kernel)
    {
        var registry = new EventIndexRegistry();
        var subscription = Assert.Single(kernel.Manifest.Subscriptions);
        Assert.True(registry.Register(
            server.Events.Resolve<AttackEvent>(),
            kernel,
            subscription.IndexedPredicates,
            subscription.IndexCoversPredicate));
        return registry;
    }

    private static PluginPackage TamperIndexCoverage(PluginPackage package)
    {
        var subscription = Assert.Single(package.Manifest.Subscriptions);
        var attackerPredicate = subscription.IndexedPredicates.Single(p => p.Path == nameof(AttackEvent.AttackerId));
        return package with
        {
            Manifest = package.Manifest with
            {
                Subscriptions =
                [
                    subscription with
                    {
                        IndexedPredicates = [attackerPredicate],
                        IndexCoversPredicate = true
                    }
                ]
            }
        };
    }

    private static PluginPackage TamperIndexPredicates(PluginPackage package)
    {
        var subscription = Assert.Single(package.Manifest.Subscriptions);
        return package with
        {
            Manifest = package.Manifest with
            {
                Subscriptions =
                [
                    subscription with
                    {
                        IndexedPredicates =
                        [
                            new IndexedPredicate(
                                nameof(AttackEvent.Damage),
                                IndexPredicateOperator.GreaterThanOrEqual,
                                999,
                                "int")
                        ],
                        IndexCoversPredicate = true
                    }
                ]
            }
        };
    }

    private static PluginPackage RemoveIndexPredicates(PluginPackage package)
    {
        var subscription = Assert.Single(package.Manifest.Subscriptions);
        return package with
        {
            Manifest = package.Manifest with
            {
                Subscriptions =
                [
                    subscription with
                    {
                        IndexedPredicates = [],
                        IndexCoversPredicate = false
                    }
                ]
            }
        };
    }

    private static PluginPackage GeneratedAttackPackage()
    {
        const string chain = """
            subscriptions.On<global::DotBoxD.Kernels.Game.Server.Abstractions.Events.AttackEvent>()
                .Where(e => e.AttackerId == "player-1" && e.Damage >= 5)
                .Select(e => e.TargetId)
                .Run((targetId, ctx) => ctx.Messages.Send(targetId, "indexed-taunt:inline"));
            """;

        var source = $$"""
            using DotBoxD.Plugins.Runtime;

            namespace ChainSample;

            public static class Usage
            {
                public static void Configure(SubscriptionRegistry subscriptions)
                    => {{chain}}
            }
            """;

        var assembly = Compile(source);
        var packageType = assembly.GetTypes().Single(type =>
            type.Name.StartsWith("HookChain_", StringComparison.Ordinal) &&
            type.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes) is not null);
        return (PluginPackage)packageType
            .GetMethod("Create", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes)!
            .Invoke(null, null)!;
    }

    private static Assembly Compile(string source)
    {
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview)
            .WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "DotBoxD.Plugins.Generated")]);
        var compilation = CSharpCompilation.Create(
            "DotBoxDEventIndexTrustBoundaryTest",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(AttackEvent).Assembly.Location)),
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

    private static SandboxPolicy ChainPolicy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .GrantHostMessageWrite()
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
        => (((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [])
            .Select(reference => MetadataReference.CreateFromFile(reference));

    private sealed class RecordingMessageSink : IPluginMessageSink
    {
        private readonly ConcurrentQueue<PluginMessage> _messages = [];

        public IReadOnlyCollection<PluginMessage> Messages => _messages.ToArray();

        public void Send(string targetId, string message) => _messages.Enqueue(new PluginMessage(targetId, message));

        public ValueTask SendAsync(string targetId, string message, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Send(targetId, message);
            return ValueTask.CompletedTask;
        }
    }
}
