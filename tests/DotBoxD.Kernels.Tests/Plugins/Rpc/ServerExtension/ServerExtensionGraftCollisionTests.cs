using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionGraftCollisionTests
{
    [Fact]
    public void Duplicate_direct_graft_methods_in_same_namespace_report_DBXK115()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using DotBoxD.Abstractions;
            using DotBoxD.Services.Attributes;

            namespace Sample;

            [RpcService]
            public interface IRemoteMonsterControl
            {
            }

            [ServerExtension(typeof(IRemoteMonsterControl), "first")]
            public sealed partial class FirstKernel
            {
                [ServerExtensionMethod(typeof(IRemoteMonsterControl))]
                public int Kill(int monsterId, HookContext ctx)
                {
                    return monsterId;
                }
            }

            [ServerExtension(typeof(IRemoteMonsterControl), "second")]
            public sealed partial class SecondKernel
            {
                [ServerExtensionMethod(typeof(IRemoteMonsterControl))]
                public int Kill(int id, HookContext ctx)
                {
                    return id;
                }
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK115" &&
                 d.GetMessage().Contains("Kill(int)", StringComparison.Ordinal) &&
                 d.GetMessage().Contains("FirstKernel", StringComparison.Ordinal));
    }

    [Fact]
    public void Duplicate_direct_graft_methods_in_different_namespaces_are_allowed()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using DotBoxD.Abstractions;
            using DotBoxD.Services.Attributes;

            namespace Domain
            {
                [RpcService]
                public interface IRemoteMonsterControl
                {
                }
            }

            namespace Sample.One
            {
                [ServerExtension(typeof(Domain.IRemoteMonsterControl), "first")]
                public sealed partial class FirstKernel
                {
                    [ServerExtensionMethod(typeof(Domain.IRemoteMonsterControl))]
                    public int Kill(int monsterId, HookContext ctx)
                    {
                        return monsterId;
                    }
                }
            }

            namespace Sample.Two
            {
                [ServerExtension(typeof(Domain.IRemoteMonsterControl), "second")]
                public sealed partial class SecondKernel
                {
                    [ServerExtensionMethod(typeof(Domain.IRemoteMonsterControl))]
                    public int Kill(int id, HookContext ctx)
                    {
                        return id;
                    }
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id == "DBXK115");
    }
}
