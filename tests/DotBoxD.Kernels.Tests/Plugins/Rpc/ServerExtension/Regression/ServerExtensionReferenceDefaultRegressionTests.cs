using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed partial class ServerExtensionSurpriseRegressionTests
{
    [Fact]
    public void Omitted_null_reference_default_host_binding_argument_reports_DBXK100()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public interface IGameWorld
            {
                [HostBinding("host.read", "game.read", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                int Read(string id = null!);
            }

            [ServerExtension("null-default")]
            public sealed partial class NullDefaultKernel
            {
                public int Read(HookContext ctx)
                {
                    return ctx.Host<IGameWorld>().Read();
                }
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("cannot omit reference parameter", StringComparison.Ordinal));
    }
}
