using System.Reflection;
using static DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.HookChainRuntimeTestCompiler;
using HostSupport = DotBoxD.Kernels.Tests.PluginAnalyzer.HostBinding.PluginAnalyzerHostBindingTestSupport;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed class HookChainInterceptorRuntimeSurpriseTests
{
    private const string LocalHostSelectSource = """
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins.Runtime;

        namespace ChainSample;

        public interface IScalarWorld
        {
            [HostBinding("host.probe.getValue", "probe.read.value", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
            int GetValue(string id);
        }

        public static class LocalHostSelectUsage
        {
            public static void Configure(HookRegistry hooks)
                => hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                    .Select((e, ctx) => ctx.Host<IScalarWorld>().GetValue(e.MonsterId))
                    .Run((value, ctx) => ctx.Messages.Send("probe", "calm"));
        }
        """;

    private const string LocalSubscriptionHostSelectSource = """
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins.Runtime;

        namespace ChainSample;

        public interface IScalarWorld
        {
            [HostBinding("host.probe.getValue", "probe.read.value", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
            int GetValue(string id);
        }

        public static class LocalSubscriptionHostSelectUsage
        {
            public static void Configure(SubscriptionRegistry subscriptions)
                => subscriptions.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                    .Select((e, ctx) => ctx.Host<IScalarWorld>().GetValue(e.MonsterId))
                    .Run((value, ctx) => ctx.Messages.Send("subscription-probe", "calm"));
        }
        """;

    [Fact]
    public async Task Generated_stage_interceptor_does_not_execute_host_marker_select_as_native_code()
    {
        var assembly = Compile(LocalHostSelectSource, enableInterceptors: true);

        var messages = new InMemoryPluginMessageSink();
        using var server = DotBoxD.Plugins.PluginServer.Create(
            messages,
            configureHost: HostSupport.AddProbeBindings,
            defaultPolicy: HostSupport.ProbeReadPolicy());
        var configure = assembly.GetType("ChainSample.LocalHostSelectUsage")!
            .GetMethod("Configure", BindingFlags.Public | BindingFlags.Static)!;
        configure.Invoke(null, [server.Hooks]);

        await server.Hooks.PublishAsync(new ChainAggroEvent("monster-1", 3));

        var message = Assert.Single(messages.Messages);
        Assert.Equal("probe", message.TargetId);
        Assert.Equal("calm", message.Message);
    }

    [Fact]
    public async Task Generated_subscription_stage_interceptor_does_not_execute_host_marker_select_as_native_code()
    {
        var assembly = Compile(LocalSubscriptionHostSelectSource, enableInterceptors: true);

        var messages = new RecordingMessageSink();
        using var server = DotBoxD.Plugins.PluginServer.Create(
            messages,
            configureHost: HostSupport.AddProbeBindings,
            defaultPolicy: HostSupport.ProbeReadPolicy());
        var configure = assembly.GetType("ChainSample.LocalSubscriptionHostSelectUsage")!
            .GetMethod("Configure", BindingFlags.Public | BindingFlags.Static)!;
        configure.Invoke(null, [server.Subscriptions]);

        server.Subscriptions.Publish(new ChainAggroEvent("monster-1", 3));

        var message = await messages.FirstMessage.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("subscription-probe", message.TargetId);
        Assert.Equal("calm", message.Message);
    }

    private sealed class RecordingMessageSink : IPluginMessageSink
    {
        private readonly TaskCompletionSource<PluginMessage> _firstMessage =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyCollection<PluginMessage> Messages =>
            _firstMessage.Task.IsCompletedSuccessfully ? [_firstMessage.Task.Result] : [];

        public Task<PluginMessage> FirstMessage => _firstMessage.Task;

        public void Send(string targetId, string message)
            => _firstMessage.TrySetResult(new PluginMessage(targetId, message));

        public ValueTask SendAsync(string targetId, string message, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Send(targetId, message);
            return ValueTask.CompletedTask;
        }
    }
}
