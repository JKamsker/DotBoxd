using System.Collections.Concurrent;
using System.Reflection;
using DotBoxD.Kernels.Policies;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Plugins.Policies;
using DotBoxD.Plugins.Runtime;
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
    public async Task Publish_uses_pipeline_registered_after_an_earlier_miss_or_publish()
    {
        var firstRun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstRunAgain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstRuns = 0;
        using var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: ChainPolicy());

        server.Subscriptions.Publish(new ChainAggroEvent("monster-0", 3));

        server.Subscriptions.On<ChainAggroEvent>().RunLocal(_ =>
        {
            if (Interlocked.Increment(ref firstRuns) == 1)
            {
                firstRun.SetResult();
            }
            else
            {
                firstRunAgain.SetResult();
            }
        });

        server.Subscriptions.Publish(new ChainAggroEvent("monster-1", 3));
        await firstRun.Task.WaitAsync(TimeSpan.FromSeconds(5));

        server.Subscriptions.On<ChainAggroEvent, AggroDispatchContext>(
            ctx => new AggroDispatchContext(ctx)).RunLocal((_, _) => secondRun.SetResult());

        server.Subscriptions.Publish(new ChainAggroEvent("monster-2", 3));

        await firstRunAgain.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await secondRun.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task A_throwing_handler_is_reported_to_the_fault_observer()
    {
        var reported = new TaskCompletionSource<SubscriptionDeliveryFault>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var server = DotBoxD.Plugins.PluginServer.Create(
            defaultPolicy: ChainPolicy(),
            onSubscriptionFault: fault => reported.TrySetResult(fault));
        server.Subscriptions.On<ChainAggroEvent>()
            .RunLocal(_ => throw new InvalidOperationException("boom"));

        server.Subscriptions.Publish(new ChainAggroEvent("monster-1", 3));

        var fault = await reported.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(SubscriptionDeliveryStage.Handler, fault.Stage);
        Assert.Equal(typeof(ChainAggroEvent), fault.EventType);
        Assert.Equal("boom", Assert.IsType<InvalidOperationException>(fault.Exception).Message);
    }

    [Fact]
    public async Task A_throwing_filter_is_reported_to_the_fault_observer()
    {
        var reported = new TaskCompletionSource<SubscriptionDeliveryFault>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var server = DotBoxD.Plugins.PluginServer.Create(
            defaultPolicy: ChainPolicy(),
            onSubscriptionFault: fault => reported.TrySetResult(fault));
        server.Subscriptions.On<ChainAggroEvent>()
            .Where((Func<ChainAggroEvent, bool>)(_ => throw new InvalidOperationException("bad filter")))
            .RunLocal(_ => { });

        server.Subscriptions.Publish(new ChainAggroEvent("monster-1", 3));

        var fault = await reported.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(SubscriptionDeliveryStage.Filter, fault.Stage);
        Assert.Equal("bad filter", Assert.IsType<InvalidOperationException>(fault.Exception).Message);
    }

    [Fact]
    public async Task Pre_canceled_publish_does_not_run_subscription_handlers()
    {
        var ran = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: ChainPolicy());
        server.Subscriptions.On<ChainAggroEvent>().RunLocal(_ => ran.SetResult());

        server.Subscriptions.Publish(new ChainAggroEvent("monster-1", 3), cancellation.Token);

        await AssertNoFaultAsync(ran.Task);
    }

    [Fact]
    public async Task Handler_caller_cancellation_is_not_reported_to_the_fault_observer()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reported = new TaskCompletionSource<SubscriptionDeliveryFault>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        using var server = DotBoxD.Plugins.PluginServer.Create(
            defaultPolicy: ChainPolicy(),
            onSubscriptionFault: fault => reported.TrySetResult(fault));
        server.Subscriptions.On<ChainAggroEvent>().RunLocal((_, ctx) =>
        {
            started.SetResult();
            cancellation.Cancel();
            ctx.CancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        });

        server.Subscriptions.Publish(new ChainAggroEvent("monster-1", 3), cancellation.Token);

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await AssertNoFaultAsync(reported.Task);
    }

    [Fact]
    public async Task Filter_caller_cancellation_is_not_reported_to_the_fault_observer()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reported = new TaskCompletionSource<SubscriptionDeliveryFault>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        using var server = DotBoxD.Plugins.PluginServer.Create(
            defaultPolicy: ChainPolicy(),
            onSubscriptionFault: fault => reported.TrySetResult(fault));
        server.Subscriptions.On<ChainAggroEvent>()
            .Where((ChainAggroEvent _, HookContext ctx) =>
            {
                started.SetResult();
                cancellation.Cancel();
                ctx.CancellationToken.ThrowIfCancellationRequested();
                return true;
            })
            .RunLocal(_ => { });

        server.Subscriptions.Publish(new ChainAggroEvent("monster-1", 3), cancellation.Token);

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await AssertNoFaultAsync(reported.Task);
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

    [Fact]
    public async Task Staged_UseGeneratedChain_honors_runtime_stage_filter()
    {
        var assembly = Compile(LoweredChainSource);
        var package = HookChainRuntimeTestCompiler.PackageFrom(assembly);
        var messages = new RecordingMessageSink();
        using var server = DotBoxD.Plugins.PluginServer.Create(messages, defaultPolicy: ChainPolicy());
        server.Subscriptions.On<ChainAggroEvent>()
            .Select(e => e.MonsterId)
            .Where(_ => false)
            .UseGeneratedChain(package);

        server.Subscriptions.Publish(new ChainAggroEvent("monster-1", 3));

        await AssertNoFaultAsync(messages.FirstMessage);
        Assert.Empty(messages.Messages);
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

    private static async Task AssertNoFaultAsync(Task task)
    {
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromMilliseconds(150)));
        Assert.NotSame(task, completed);
    }

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

    private sealed record AggroDispatchContext(HookContext Raw);
}
