using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionClientDtoConstructorValidationTests
{
    [Fact]
    public void Generated_client_rejects_private_matching_dto_constructor()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using System.Threading.Tasks;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed class KillResult
            {
                public KillResult()
                {
                }

                private KillResult(int monsterId, bool success)
                {
                    MonsterId = monsterId;
                    Success = success;
                }

                public int MonsterId { get; init; }
                public bool Success { get; init; }
            }

            public interface IKillService
            {
                ValueTask<KillResult> KillAsync(int monsterId);
            }

            [ServerExtension("kill", typeof(IKillService))]
            public sealed partial class KillKernel
            {
                public KillResult Kill(int monsterId, HookContext ctx)
                {
                    return new KillResult { MonsterId = monsterId, Success = true };
                }
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("constructor matching its public fields", StringComparison.Ordinal));
    }
}
