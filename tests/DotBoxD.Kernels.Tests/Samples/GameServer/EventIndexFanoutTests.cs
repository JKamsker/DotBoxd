extern alias GameServerAbstractions;

using System.Collections.Concurrent;
using System.Reflection;
using DotBoxD.Kernels.Policies;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Plugins.Indexing;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Policies;
using GameServerAbstractions::DotBoxD.Kernels.Game.Server.Abstractions.Events;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.Samples.GameServer;

/// <summary>
/// Issue #47 acceptance: an indexed subscription's manifest lets the host prefilter events through its
/// <see cref="EventIndexMatcher{TEvent}"/> so the verified IR (the lowered <c>ShouldHandle</c>) runs only
/// for events the cheap index accepts. Here 100 attacks are published, only 3 share the indexed
/// <c>AttackerId == "player-1"</c> bucket, and we prove the lowered handler ran exactly 3 times — the
/// other 97 never entered the sandbox.
/// </summary>
public sealed class EventIndexFanoutTests
{
    [Fact]
    public async Task Host_index_prefilter_runs_the_lowered_predicate_only_for_matching_events()
    {
        var package = GeneratedAttackPackage();
        var subscription = Assert.Single(package.Manifest.Subscriptions);

        // The host compiles the manifest metadata into its dispatch index. Both leaves hit [EventIndexKey]
        // properties, so both are honored and the manifest reports full coverage.
        var matcher = EventIndexMatcher<AttackEvent>.Create(subscription.IndexedPredicates);
        Assert.True(subscription.IndexCoversPredicate);
        Assert.Equal(2, matcher.HonoredPredicates.Count);

        var sink = new RecordingMessageSink();
        using var server = DotBoxD.Plugins.PluginServer.Create(sink, defaultPolicy: ChainPolicy());
        var adapter = server.Events.Resolve<AttackEvent>();
        var kernel = await server.InstallAsync(package, ChainPolicy());

        var loweredPredicateRuns = 0;
        var prefiltered = 0;
        foreach (var attack in Attacks(total: 100, matching: 3))
        {
            // The cheap, host-side index check happens before any sandbox entry.
            if (!matcher.CouldMatch(attack))
            {
                prefiltered++;
                continue;
            }

            // Index accepted → run the verified IR as the correctness authority, exactly as a host would.
            loweredPredicateRuns++;
            if (await kernel.ShouldHandleAsync(adapter, attack))
            {
                await kernel.HandleAsync(adapter, attack);
            }
        }

        Assert.Equal(97, prefiltered);
        Assert.Equal(3, loweredPredicateRuns);
        Assert.Equal(3, sink.Messages.Count);
        Assert.All(sink.Messages, message => Assert.Equal("indexed-taunt:inline", message.Message));
        Assert.All(sink.Messages, message => Assert.Equal("player-2", message.TargetId));
    }

    [Fact]
    public void Matcher_only_honors_predicates_whose_path_is_an_index_key()
    {
        // AttackerLevel is deliberately NOT an [EventIndexKey], so a predicate over it cannot be served
        // from the index and is left to the verified IR.
        var predicates = new[]
        {
            new IndexedPredicate("AttackerId", IndexPredicateOperator.Equals, "player-1", "string"),
            new IndexedPredicate("AttackerLevel", IndexPredicateOperator.GreaterThanOrEqual, 9, "int"),
        };

        var matcher = EventIndexMatcher<AttackEvent>.Create(predicates);

        var honored = Assert.Single(matcher.HonoredPredicates);
        Assert.Equal("AttackerId", honored.Path);

        // A high-level attacker who is "player-1" still passes the index (AttackerLevel is not indexed),
        // and a different attacker is rejected regardless of level.
        Assert.True(matcher.CouldMatch(new AttackEvent("player-1", "player-2", 3, 1)));
        Assert.False(matcher.CouldMatch(new AttackEvent("monster-1", "player-2", 99, 12)));
    }

    [Fact]
    public async Task Index_registry_dispatches_verified_ir_only_for_events_that_pass_the_index()
    {
        // Issue #49/#50: the framework EventIndexRegistry is the live-dispatch mechanism a host (and the
        // GameServer sample) routes indexed subscriptions through. Publishing 100 attacks where only 3 share
        // the indexed bucket must prefilter 97 before any sandbox entry and dispatch the verified IR for
        // exactly the 3 survivors.
        var package = GeneratedAttackPackage();
        var subscription = Assert.Single(package.Manifest.Subscriptions);

        var sink = new RecordingMessageSink();
        using var server = DotBoxD.Plugins.PluginServer.Create(sink, defaultPolicy: ChainPolicy());
        var adapter = server.Events.Resolve<AttackEvent>();
        var kernel = await server.InstallAsync(package, ChainPolicy());

        var registry = new EventIndexRegistry();
        Assert.True(registry.Register(adapter, kernel, subscription.IndexedPredicates, subscription.IndexCoversPredicate));

        foreach (var attack in Attacks(total: 100, matching: 3))
        {
            registry.Publish(attack);
        }

        var stats = registry.Stats;
        Assert.Equal(100, stats.Considered);
        Assert.Equal(97, stats.Prefiltered);
        Assert.Equal(3, stats.Dispatched);
        Assert.Equal(stats.Considered, stats.Prefiltered + stats.Dispatched);

        await registry.DrainAsync();
        Assert.Equal(3, sink.Messages.Count);
        Assert.All(sink.Messages, message => Assert.Equal("indexed-taunt:inline", message.Message));
        Assert.All(sink.Messages, message => Assert.Equal("player-2", message.TargetId));
    }

    [Fact]
    public async Task Index_registry_declines_subscriptions_with_no_indexed_field()
    {
        // AttackerLevel is not an [EventIndexKey], so a predicate over it alone cannot be served from the
        // index; the registry declines registration and the host keeps it on the broad pipeline.
        var package = GeneratedAttackPackage();
        var sink = new RecordingMessageSink();
        using var server = DotBoxD.Plugins.PluginServer.Create(sink, defaultPolicy: ChainPolicy());
        var adapter = server.Events.Resolve<AttackEvent>();
        var kernel = await server.InstallAsync(package, ChainPolicy());

        var registry = new EventIndexRegistry();
        var predicates = new[]
        {
            new IndexedPredicate("AttackerLevel", IndexPredicateOperator.GreaterThanOrEqual, 9, "int"),
        };

        Assert.False(registry.Register(adapter, kernel, predicates, indexCoversPredicate: false));

        registry.Publish(new AttackEvent("monster-1", "player-2", 9, 12));
        Assert.Equal(0, registry.Stats.Considered);
    }

    private static IEnumerable<AttackEvent> Attacks(int total, int matching)
    {
        for (var i = 0; i < total; i++)
        {
            var attackerId = i < matching ? "player-1" : $"monster-{i}";
            yield return new AttackEvent(attackerId, "player-2", Damage: 7, AttackerLevel: 8);
        }
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
        var create = packageType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes)!;
        return (PluginPackage)create.Invoke(null, null)!;
    }

    private static Assembly Compile(string source)
    {
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview)
            .WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "DotBoxD.Plugins.Generated")]);
        var compilation = CSharpCompilation.Create(
            "DotBoxDEventIndexFanoutTest",
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
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }

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
