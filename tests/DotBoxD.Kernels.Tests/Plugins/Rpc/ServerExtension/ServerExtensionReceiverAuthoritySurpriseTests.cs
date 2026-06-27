using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionReceiverAuthoritySurpriseTests
{
    [Fact]
    public void This_qualified_grafted_receiver_field_threads_receiver_id_to_host_call()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create("""
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace Sample;

            [DotBoxDService]
            public interface IRemoteMonster
            {
                string Id { get; }

                [HostCapability("game.world.monster.read.threat", HostBindingEffect.HostStateRead)]
                int Threat();
            }

            [ServerExtension(typeof(IRemoteMonster), "this-qualified-threat")]
            public sealed partial class ThisQualifiedThreatKernel
            {
                private readonly IRemoteMonster _monster;

                public ThisQualifiedThreatKernel(IRemoteMonster monster) => _monster = monster;

                public int Read(HookContext ctx) => this._monster.Threat();
            }
            """, "Sample.ThisQualifiedThreatPluginPackage");

        AssertReceiverIdThreadedToHostCall(package);
    }

    [Fact]
    public void Inherited_grafted_receiver_field_threads_receiver_id_to_host_call()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create("""
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace Sample;

            [DotBoxDService]
            public interface IRemoteMonster
            {
                string Id { get; }

                [HostCapability("game.world.monster.read.threat", HostBindingEffect.HostStateRead)]
                int Threat();
            }

            public abstract class ThreatKernelBase
            {
                protected readonly IRemoteMonster Monster;

                protected ThreatKernelBase(IRemoteMonster monster) => Monster = monster;
            }

            [ServerExtension(typeof(IRemoteMonster), "inherited-threat")]
            public sealed partial class InheritedThreatKernel : ThreatKernelBase
            {
                public InheritedThreatKernel(IRemoteMonster monster) : base(monster)
                {
                }

                public int Read(HookContext ctx) => Monster.Threat();
            }
            """, "Sample.InheritedThreatPluginPackage");

        AssertReceiverIdThreadedToHostCall(package);
    }

    private static void AssertReceiverIdThreadedToHostCall(PluginPackage package)
    {
        var function = Assert.Single(package.Module.Functions);
        var parameter = Assert.Single(function.Parameters);
        Assert.Equal("__receiverId", parameter.Name);

        var returned = Assert.IsType<ReturnStatement>(Assert.Single(function.Body));
        var hostCall = Assert.IsType<CallExpression>(returned.Value);
        var argument = Assert.IsType<VariableExpression>(Assert.Single(hostCall.Arguments));
        Assert.Equal("__receiverId", argument.Name);
    }
}
