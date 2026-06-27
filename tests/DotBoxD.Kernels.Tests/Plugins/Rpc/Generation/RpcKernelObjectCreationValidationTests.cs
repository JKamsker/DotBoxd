using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins.Json;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class RpcKernelObjectCreationValidationTests
{
    [Fact]
    public void Kernel_rpc_service_supports_mixed_constructor_and_object_initializer()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create("""
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
            """, "Sample.MixedCreationPluginPackage");

        var json = PluginPackageJsonSerializer.Export(package);
        Assert.Contains("\"call\":\"record.new\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Kernel_rpc_service_supports_constructor_with_trailing_optional_parameter()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create("""
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed class KillResult
            {
                public KillResult(int monsterId, bool success = true)
                {
                    MonsterId = monsterId;
                    Success = success;
                }

                public int MonsterId { get; }
                public bool Success { get; }
            }

            [ServerExtension("optional-constructor")]
            public sealed partial class OptionalConstructorKernel
            {
                public KillResult Build(int monsterId, HookContext ctx)
                {
                    return new KillResult(monsterId);
                }
            }
            """, "Sample.OptionalConstructorPluginPackage");

        var json = PluginPackageJsonSerializer.Export(package);
        Assert.Contains("\"call\":\"record.new\"", json, StringComparison.Ordinal);
    }
}
