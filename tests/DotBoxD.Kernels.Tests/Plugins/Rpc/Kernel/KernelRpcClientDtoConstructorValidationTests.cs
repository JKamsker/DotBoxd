using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionClientDtoConstructorValidationTests
{
    private const string PrivateConstructorInitDtoSource = """
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
        """;

    [Fact]
    public void Generated_client_reconstructs_dto_via_settable_properties_when_the_matching_constructor_is_private()
    {
        // The full-field constructor is private and unusable from generated code, but the DTO exposes a
        // public parameterless constructor and init-only properties — so the client reconstructs it through
        // an object initializer (matching the runtime marshaller's fallback) instead of failing generation.
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(PrivateConstructorInitDtoSource);

        Assert.NotNull(assembly.GetType("Sample.KillKernelServerExtensionClient", throwOnError: true));
    }
}
