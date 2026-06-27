namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed partial class RemoteRunLocalChainRuntimeTests
{
    private const string AnonymousRemoteRunTerminalSource = Prelude + """
        public static class AnonymousRemoteRunTerminalUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.EncounterEvent>().Where(e => e.Distance <= 4)
                    .Select(e => new { Id = e.EncounterId, Zone = e.Zone })
                    .Run((x, ctx) => ctx.Messages.Send(x.Id.ToString(), x.Zone));
        }
        """;

    private const string AnonymousRemoteSubscriptionRunTerminalSource = Prelude + """
        public static class AnonymousRemoteSubscriptionRunTerminalUsage
        {
            public static void Configure(RemoteSubscriptionRegistry subscriptions)
                => subscriptions.On<Ev.EncounterEvent>().Where(e => e.Distance <= 4)
                    .Select(e => new { Id = e.EncounterId, Zone = e.Zone })
                    .Run((x, ctx) => ctx.Messages.Send(x.Id.ToString(), x.Zone));
        }
        """;

    [Fact]
    public void Anonymous_terminal_projection_Run_emits_a_generic_interceptor_that_compiles()
    {
        _ = Compile(AnonymousRemoteRunTerminalSource, enableInterceptors: true);
        var generated = GeneratedSource(AnonymousRemoteRunTerminalSource);

        Assert.Contains("Intercept_0<TEvent, TCurrent>", generated, StringComparison.Ordinal);
        Assert.Contains(".UseGeneratedChain(", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("<anonymous type", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Anonymous_terminal_projection_subscription_Run_emits_a_generic_interceptor_that_compiles()
    {
        _ = Compile(AnonymousRemoteSubscriptionRunTerminalSource, enableInterceptors: true);
        var generated = GeneratedSource(AnonymousRemoteSubscriptionRunTerminalSource);

        Assert.Contains("Intercept_0<TEvent, TCurrent>", generated, StringComparison.Ordinal);
        Assert.Contains(".UseGeneratedChain(", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("<anonymous type", generated, StringComparison.Ordinal);
    }
}
