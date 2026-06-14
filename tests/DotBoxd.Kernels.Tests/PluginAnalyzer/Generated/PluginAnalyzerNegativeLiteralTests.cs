using DotBoxd.Plugins;

namespace DotBoxd.Kernels.Tests;

public sealed class PluginAnalyzerNegativeLiteralTests
{
    [Fact]
    public async Task Generated_package_lowers_negative_wide_literals()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create("""
            using DotBoxd.Plugins;
            using DotBoxd.Abstractions;

            namespace Sample;

            public sealed record NegativeLiteralEvent(
                string TargetId,
                string Message,
                long Sequence,
                double Ratio,
                int Amount);

            [Plugin("generated-negative-literals")]
            public sealed partial class NegativeLiteralKernel : IEventKernel<NegativeLiteralEvent>
            {
                public bool ShouldHandle(NegativeLiteralEvent e, HookContext ctx)
                    => e.Sequence == -5L && e.Ratio == -1.5D && e.Amount > -1;

                public void Handle(NegativeLiteralEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.Message);
            }
            """, "Sample.NegativeLiteralPluginPackage");
        var server = PluginAddendumTestPolicies.CreateServer();
        var kernel = await server.InstallAsync(package);
        var adapter = new NegativeLiteralEventAdapter();

        Assert.True(await kernel.ShouldHandleAsync(adapter, Event(-5L, -1.5D, 0)));
        Assert.False(await kernel.ShouldHandleAsync(adapter, Event(5L, -1.5D, 0)));
        Assert.False(await kernel.ShouldHandleAsync(adapter, Event(-5L, 1.5D, 0)));
        Assert.False(await kernel.ShouldHandleAsync(adapter, Event(-5L, -1.5D, -1)));
    }

    private static NegativeLiteralEvent Event(long sequence, double ratio, int amount)
        => new("player-1", "matched", sequence, ratio, amount);

    private sealed record NegativeLiteralEvent(
        string TargetId,
        string Message,
        long Sequence,
        double Ratio,
        int Amount);

    private sealed class NegativeLiteralEventAdapter : IPluginEventAdapter<NegativeLiteralEvent>
    {
        public string EventName => nameof(NegativeLiteralEvent);

        public IReadOnlyList<Parameter> Parameters { get; } = [
            new("e_TargetId", SandboxType.String),
            new("e_Message", SandboxType.String),
            new("e_Sequence", SandboxType.I64),
            new("e_Ratio", SandboxType.F64),
            new("e_Amount", SandboxType.I32)
        ];

        public IReadOnlyList<SandboxValue> ToSandboxValues(NegativeLiteralEvent e)
            => [
                SandboxValue.FromString(e.TargetId),
                SandboxValue.FromString(e.Message),
                SandboxValue.FromInt64(e.Sequence),
                SandboxValue.FromDouble(e.Ratio),
                SandboxValue.FromInt32(e.Amount)
            ];
    }
}
