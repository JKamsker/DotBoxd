using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginAnalyzerEnumerableDtoValidationTests
{
    [Fact]
    public void Generator_rejects_unsupported_enumerable_event_property_type()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using System.Collections.Generic;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;

            namespace Sample;

            public sealed record BadEvent(Queue<int> Values);

            [Plugin("bad-enumerable-event")]
            public sealed partial class BadKernel : IEventKernel<BadEvent>
            {
                public bool ShouldHandle(BadEvent e, HookContext ctx) => true;

                public void Handle(BadEvent e, HookContext ctx)
                    => ctx.Messages.Send("player-1", "message");
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("Queue", StringComparison.Ordinal) &&
                 d.GetMessage().Contains("unsupported", StringComparison.OrdinalIgnoreCase));
    }
}
