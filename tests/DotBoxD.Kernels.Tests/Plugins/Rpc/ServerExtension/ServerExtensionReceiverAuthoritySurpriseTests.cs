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

    [Fact]
    public void Cast_grafted_receiver_field_threads_receiver_id_to_host_call()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create("""
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace Sample;

            [DotBoxDService]
            public interface IRemoteEntity
            {
                string Id { get; }

                [HostCapability("game.world.entity.read.threat", HostBindingEffect.HostStateRead)]
                int Threat();
            }

            [DotBoxDService]
            public interface IRemoteMonster : IRemoteEntity;

            [ServerExtension(typeof(IRemoteMonster), "cast-threat")]
            public sealed partial class CastThreatKernel
            {
                private readonly IRemoteMonster _monster;

                public CastThreatKernel(IRemoteMonster monster) => _monster = monster;

                public int Read(HookContext ctx) => ((IRemoteEntity)_monster).Threat();
            }
            """, "Sample.CastThreatPluginPackage");

        AssertReceiverIdThreadedToHostCall(package);
    }

    [Fact]
    public void Block_local_shadowing_grafted_receiver_field_does_not_rebind_receiver_authority()
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

            [DotBoxDService]
            public interface IMonsterDirectory
            {
                IRemoteMonster Get(string id);
            }

            [ServerExtension(typeof(IRemoteMonster), "shadow-threat")]
            public sealed partial class ShadowThreatKernel
            {
                private readonly IRemoteMonster _monster;

                public ShadowThreatKernel(IRemoteMonster monster) => _monster = monster;

                public int Read(string otherId, HookContext ctx)
                {
                    if (true)
                    {
                        var _monster = ctx.Host<IMonsterDirectory>().Get(otherId);
                        var ignored = _monster.Threat();
                    }

                    return _monster.Threat();
                }
            }
        """, "Sample.ShadowThreatPluginPackage");

        var function = Assert.Single(package.Module.Functions);
        Assert.Equal(["__receiverId", "otherId"], function.Parameters.Select(parameter => parameter.Name));

        var returned = Assert.IsType<ReturnStatement>(function.Body.Last());
        var hostCall = Assert.IsType<CallExpression>(returned.Value);
        var argument = Assert.IsType<VariableExpression>(Assert.Single(hostCall.Arguments));
        Assert.Equal("__receiverId", argument.Name);
    }

    [Fact]
    public void Receiver_graft_rejects_payload_parameter_named_receiver_id()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
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

            [ServerExtension(typeof(IRemoteMonster), "receiver-name-collision")]
            public sealed partial class CollisionKernel
            {
                private readonly IRemoteMonster _monster;

                public CollisionKernel(IRemoteMonster monster) => _monster = monster;

                [ServerExtensionMethod(typeof(IRemoteMonster))]
                public int Read(string __receiverId, HookContext ctx) => _monster.Threat();
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("__receiverId", StringComparison.Ordinal));
        Assert.DoesNotContain(
            diagnostics,
            d => d.Id == "DBXK117" || d.Id.StartsWith("CS", StringComparison.Ordinal));
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
