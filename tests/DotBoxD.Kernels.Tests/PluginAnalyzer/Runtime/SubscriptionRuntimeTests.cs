using System.Collections.Concurrent;
using System.Reflection;
using DotBoxD.Kernels.Policies;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Plugins.Policies;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed class SubscriptionRuntimeTests
{
    private const string LoweredChainSource = """
        using DotBoxD.Plugins.Runtime;

        namespace ChainSample;

        public static class Usage
        {
            public static void Configure(SubscriptionRegistry subscriptions)
                => subscriptions.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                    .Where(e => e.Distance <= 5)
                    .Select(e => e.MonsterId)
                    .Run((id, ctx) => ctx.Messages.Send(id, "calm"));
        }
        """;

    [Fact]
    public async Task PublishAsync_does_not_wait_for_handlers_to_finish()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: ChainPolicy());
        server.Subscriptions.On<ChainAggroEvent>()
            .RunLocal(async _ =>
            {
                started.SetResult();
                await release.Task.ConfigureAwait(false);
            });

        await server.Subscriptions.PublishAsync(new ChainAggroEvent("monster-1", 3));

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(release.Task.IsCompleted);

        release.SetResult();
    }

    [Fact]
    public async Task Handler_exceptions_are_isolated_from_other_subscribers()
    {
        var handled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: ChainPolicy());
        server.Subscriptions.On<ChainAggroEvent>()
            .RunLocal(_ => throw new InvalidOperationException("boom"))
            .RunLocal(_ => handled.SetResult());

        server.Subscriptions.Publish(new ChainAggroEvent("monster-1", 3));

        await handled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task The_generated_interceptor_installs_a_local_subscription_chain_at_the_Run_call_site()
    {
        var assembly = Compile(LoweredChainSource);
        var messages = new RecordingMessageSink();
        using var server = DotBoxD.Plugins.PluginServer.Create(messages, defaultPolicy: ChainPolicy());
        var configure = assembly.GetType("ChainSample.Usage")!
            .GetMethod("Configure", BindingFlags.Public | BindingFlags.Static)!;

        configure.Invoke(null, [server.Subscriptions]);
        server.Subscriptions.Publish(new ChainAggroEvent("monster-1", 3));
        server.Subscriptions.Publish(new ChainAggroEvent("monster-2", 10));

        var message = await messages.FirstMessage.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("monster-1", message.TargetId);
        Assert.Equal("calm", message.Message);
        Assert.Single(messages.Messages);
    }

    private static Assembly Compile(string source)
    {
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview)
            .WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "DotBoxD.Plugins.Generated")]);
        var compilation = CSharpCompilation.Create(
            "DotBoxDSubscriptionRuntimeTest",
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
        private readonly TaskCompletionSource<PluginMessage> _firstMessage =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyCollection<PluginMessage> Messages => _messages.ToArray();

        public Task<PluginMessage> FirstMessage => _firstMessage.Task;

        public void Send(string targetId, string message)
        {
            var pluginMessage = new PluginMessage(targetId, message);
            _messages.Enqueue(pluginMessage);
            _firstMessage.TrySetResult(pluginMessage);
        }

        public ValueTask SendAsync(
            string targetId,
            string message,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Send(targetId, message);
            return ValueTask.CompletedTask;
        }
    }
}
