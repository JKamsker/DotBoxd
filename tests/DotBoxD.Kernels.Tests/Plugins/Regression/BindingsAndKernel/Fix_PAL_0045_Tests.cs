using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Regression.BindingsAndKernel;

/// <summary>
/// Regression coverage for PAL-0045: analyzer-generated packages must order metadata-only
/// event properties the same way the runtime convention adapter does.
/// </summary>
public sealed class Fix_PAL_0045_Tests
{
    private const string MetadataOnlyPluginSource = """
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels.Tests.Plugins.Regression.BindingsAndKernel;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;

        namespace Sample;

        [Plugin("metadata-event-order")]
        public sealed partial class MetadataEventOrderKernel : IEventKernel<MetadataOnlyConventionEvent>
        {
            public bool ShouldHandle(MetadataOnlyConventionEvent e, HookContext ctx)
                => e.Amount > 100;

            public void Handle(MetadataOnlyConventionEvent e, HookContext ctx)
                => ctx.Messages.Send(e.TargetId, "metadata matched");
        }
        """;

    [Fact]
    public async Task Convention_adapter_order_matches_metadata_only_generated_package()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            MetadataOnlyPluginSource,
            "Sample.MetadataEventOrderPluginPackage",
            typeof(MetadataOnlyConventionEvent));

        var messages = new InMemoryPluginMessageSink();
        var server = PluginAddendumTestPolicies.CreateServer(messages);
        var kernel = await server.InstallAsync(package);

        server.Hooks.On<MetadataOnlyConventionEvent>().Use(kernel);
        await server.Hooks.PublishAsync(new MetadataOnlyConventionEvent
        {
            TargetId = "player-1",
            Amount = 150
        });

        var message = Assert.Single(messages.Messages);
        Assert.Equal("player-1", message.TargetId);
        Assert.Equal("metadata matched", message.Message);
    }
}

public sealed class MetadataOnlyConventionEvent
{
    public string TargetId { get; init; } = "";

    public int Amount { get; init; }
}
