using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class RpcKernelObjectCreationValidationTests
{
    [Fact]
    public void Kernel_rpc_service_rejects_mixed_constructor_and_object_initializer()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed class KillResult
            {
                public KillResult(int monsterId, bool success)
                {
                    MonsterId = monsterId;
                    Success = success;
                }

                public int MonsterId { get; set; }
                public bool Success { get; set; }
            }

            [ServerExtension("mixed-creation")]
            public sealed partial class MixedCreationKernel
            {
                public KillResult Build(int monsterId, HookContext ctx)
                {
                    return new KillResult(monsterId, false) { Success = true };
                }
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("cannot combine constructor arguments and object initializers", StringComparison.Ordinal));
    }
}
