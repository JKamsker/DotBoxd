extern alias GameServerAbstractions;

using System.Collections.Concurrent;
using System.Reflection;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Plugins.Indexing;
using DotBoxD.Plugins.Policies;
using DotBoxD.Plugins.Runtime;
using GameServerAbstractions::DotBoxD.Kernels.Game.Server.Abstractions.Events;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using KernelParameter = DotBoxD.Kernels.Parameter;
using SandboxValidationException = DotBoxD.Kernels.Model.SandboxValidationException;

namespace DotBoxD.Kernels.Tests.Samples.GameServer;

public sealed partial class EventIndexCancellationTests
{
    [Fact]
    public async Task Index_registry_pre_canceled_publish_throws_before_considering_entries()
    {
        var package = GeneratedAttackPackage();
        var subscription = Assert.Single(package.Manifest.Subscriptions);
        var sink = new RecordingMessageSink();
        using var server = DotBoxD.Plugins.PluginServer.Create(sink, defaultPolicy: ChainPolicy());
        var kernel = await server.InstallAsync(package, ChainPolicy());
        var registry = new EventIndexRegistry();
        Assert.True(registry.Register(
            server.Events.Resolve<AttackEvent>(),
            kernel,
            subscription.IndexedPredicates,
            subscription.IndexCoversPredicate));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = Record.Exception(
            () => registry.Publish(new AttackEvent("player-1", "player-2", 7, 8), cancellation.Token));
        await registry.DrainAsync();

        Assert.IsType<OperationCanceledException>(exception);
        Assert.Equal(0, registry.Stats.Considered);
        Assert.Empty(sink.Messages);
    }

    [Fact]
    public async Task Index_registry_reports_verified_ir_dispatch_faults()
    {
        var reported = new TaskCompletionSource<SubscriptionDeliveryFault>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var package = GeneratedAttackPackage();
        var subscription = Assert.Single(package.Manifest.Subscriptions);
        using var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: ChainPolicy());
        var kernel = await server.InstallAsync(package, ChainPolicy());
        var registry = new EventIndexRegistry(fault => reported.TrySetResult(fault));
        Assert.True(registry.Register(
            new MismatchedAttackEventAdapter(),
            kernel,
            subscription.IndexedPredicates,
            subscription.IndexCoversPredicate));

        registry.Publish(new AttackEvent("player-1", "player-2", 7, 8));

        var fault = await reported.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(SubscriptionDeliveryStage.Filter, fault.Stage);
        Assert.Equal(typeof(AttackEvent), fault.EventType);
        var ex = Assert.IsType<SandboxValidationException>(fault.Exception);
        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK033");
    }

    [Theory]
    [InlineData(true, SubscriptionDeliveryStage.Filter)]
    [InlineData(false, SubscriptionDeliveryStage.Handler)]
    public async Task Index_registry_does_not_report_sandbox_caller_cancellation(
        bool cancelInFilter,
        SubscriptionDeliveryStage stage)
    {
        var reported = new TaskCompletionSource<SubscriptionDeliveryFault>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        using var server = DotBoxD.Plugins.PluginServer.Create(
            configureHost: builder => builder.AddBinding(CancelBinding(cancellation.Cancel)),
            defaultPolicy: ChainPolicy(),
            onSubscriptionFault: fault => reported.TrySetResult(fault));
        var package = CancellationPackage(cancelInFilter);
        var kernel = await server.InstallAsync(package, ChainPolicy());
        var subscription = Assert.Single(package.Manifest.Subscriptions);
        var registry = new EventIndexRegistry(fault => reported.TrySetResult(fault));
        Assert.True(registry.Register(
            server.Events.Resolve<AttackEvent>(),
            kernel,
            subscription.IndexedPredicates,
            subscription.IndexCoversPredicate));

        registry.Publish(new AttackEvent("player-1", "player-2", 7, 8), cancellation.Token);
        await registry.DrainAsync();

        Assert.True(cancellation.IsCancellationRequested);
        await AssertNoFaultAsync(reported.Task);
        Assert.False(reported.Task.IsCompleted, $"caller cancellation during {stage} was reported as a fault");
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
        return (PluginPackage)packageType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes)!
            .Invoke(null, null)!;
    }

    private static Assembly Compile(string source)
    {
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview)
            .WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "DotBoxD.Plugins.Generated")]);
        var compilation = CSharpCompilation.Create(
            "DotBoxDEventIndexCancellationTest",
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

    private sealed class MismatchedAttackEventAdapter : IPluginEventAdapter<AttackEvent>
    {
        public string EventName => typeof(AttackEvent).FullName!;

        public IReadOnlyList<KernelParameter> Parameters { get; } = [
            new("wrongAttackerId", SandboxType.String),
            new("e_TargetId", SandboxType.String),
            new("e_Damage", SandboxType.I32),
            new("e_AttackerLevel", SandboxType.I32)
        ];

        public IReadOnlyList<SandboxValue> ToSandboxValues(AttackEvent e)
            => [
                SandboxValue.FromString(e.AttackerId),
                SandboxValue.FromString(e.TargetId),
                SandboxValue.FromInt32(e.Damage),
                SandboxValue.FromInt32(e.AttackerLevel)
            ];
    }
}
