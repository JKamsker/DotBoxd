using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed partial class ServerExtensionLoweringSurpriseTests
{
    [Fact]
    public void Server_extension_lowers_target_typed_dto_creation()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create("""
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record KillResult(int MonsterId, bool Success);

            [ServerExtension("target-new")]
            public sealed partial class TargetNewKernel
            {
                public KillResult Build(int monsterId, HookContext ctx) => new(monsterId, true);
            }
            """, "Sample.TargetNewPluginPackage");

        Assert.Equal(
            SandboxType.Record([SandboxType.I32, SandboxType.Bool]),
            Assert.Single(package.Module.Functions).ReturnType);
    }

    [Fact]
    public void Server_extension_lowers_explicit_numeric_widening_cast()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create("""
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            [ServerExtension("numeric-cast")]
            public sealed partial class NumericCastKernel
            {
                public long Widen(int value, HookContext ctx) => (long)value;
            }
            """, "Sample.NumericCastPluginPackage");

        var returned = Assert.IsType<ReturnStatement>(Assert.Single(Assert.Single(package.Module.Functions).Body));
        var cast = Assert.IsType<CallExpression>(returned.Value);
        Assert.Equal("numeric.toI64", cast.Name);
        Assert.IsType<VariableExpression>(Assert.Single(cast.Arguments));
    }

    [Fact]
    public void Server_extension_lowers_unary_plus_expression()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create("""
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            [ServerExtension("unary-plus")]
            public sealed partial class UnaryPlusKernel
            {
                public int Echo(int value, HookContext ctx) => +value;
            }
            """, "Sample.UnaryPlusPluginPackage");

        var returned = Assert.IsType<ReturnStatement>(Assert.Single(Assert.Single(package.Module.Functions).Body));
        var variable = Assert.IsType<VariableExpression>(returned.Value);
        Assert.Equal("value", variable.Name);
    }

    [Fact]
    public async Task Server_extension_lowers_long_postfix_increment()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create("""
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            [ServerExtension("long-postfix")]
            public sealed partial class LongPostfixKernel
            {
                public long Count(HookContext ctx)
                {
                    long total = 0;
                    total++;
                    return total;
                }
            }
            """, "Sample.LongPostfixPluginPackage");

        using var server = PluginServer.Create(defaultPolicy: PurePolicy());
        var kernel = await server.InstallServerExtensionAsync(package);

        var result = await kernel.InvokeServerExtensionAsync([]);

        Assert.Equal(1L, Assert.IsType<I64Value>(result).Value);
    }

    [Fact]
    public async Task Server_extension_lowers_prefix_increment_statement()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create("""
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            [ServerExtension("prefix-increment")]
            public sealed partial class PrefixIncrementKernel
            {
                public int Count(HookContext ctx)
                {
                    var total = 0;
                    ++total;
                    return total;
                }
            }
            """, "Sample.PrefixIncrementPluginPackage");

        using var server = PluginServer.Create(defaultPolicy: PurePolicy());
        var kernel = await server.InstallServerExtensionAsync(package);

        var result = await kernel.InvokeServerExtensionAsync([]);

        Assert.Equal(1, Assert.IsType<I32Value>(result).Value);
    }

    [Fact]
    public async Task Server_extension_lowers_definite_assigned_local_without_initializer()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create("""
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            [ServerExtension("definite-assigned-local")]
            public sealed partial class DefiniteAssignedLocalKernel
            {
                public int Count(HookContext ctx)
                {
                    int total;
                    total = 1;
                    return total;
                }
            }
            """, "Sample.DefiniteAssignedLocalPluginPackage");

        using var server = PluginServer.Create(defaultPolicy: PurePolicy());
        var kernel = await server.InstallServerExtensionAsync(package);

        var result = await kernel.InvokeServerExtensionAsync([]);

        Assert.Equal(1, Assert.IsType<I32Value>(result).Value);
    }

    [Fact]
    public async Task Server_extension_preserves_definite_assignment_across_if_else()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create("""
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            [ServerExtension("branch-definite-local")]
            public sealed partial class BranchDefiniteLocalKernel
            {
                public int Pick(bool flag, HookContext ctx)
                {
                    int value;
                    if (flag)
                    {
                        value = 1;
                    }
                    else
                    {
                        value = 2;
                    }

                    return value;
                }
            }
            """, "Sample.BranchDefiniteLocalPluginPackage");

        using var server = PluginServer.Create(defaultPolicy: PurePolicy());
        var kernel = await server.InstallServerExtensionAsync(package);

        var whenTrue = await kernel.InvokeServerExtensionAsync([SandboxValue.FromBool(true)]);
        var whenFalse = await kernel.InvokeServerExtensionAsync([SandboxValue.FromBool(false)]);

        Assert.Equal(1, Assert.IsType<I32Value>(whenTrue).Value);
        Assert.Equal(2, Assert.IsType<I32Value>(whenFalse).Value);
    }

    [Fact]
    public async Task Server_extension_lowers_while_statement()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create("""
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            [ServerExtension("while-loop")]
            public sealed partial class WhileLoopKernel
            {
                public int Count(HookContext ctx)
                {
                    var total = 0;
                    while (total < 3)
                    {
                        total++;
                    }

                    return total;
                }
            }
            """, "Sample.WhileLoopPluginPackage");

        using var server = PluginServer.Create(defaultPolicy: PurePolicy());
        var kernel = await server.InstallServerExtensionAsync(package);

        var result = await kernel.InvokeServerExtensionAsync([]);

        Assert.Equal(3, Assert.IsType<I32Value>(result).Value);
    }

    private static SandboxPolicy PurePolicy()
        => SandboxPolicyBuilder.Create()
            .WithFuel(10_000)
            .WithMaxHostCalls(100)
            .WithWallTime(TimeSpan.FromSeconds(5))
            .Build();
}
