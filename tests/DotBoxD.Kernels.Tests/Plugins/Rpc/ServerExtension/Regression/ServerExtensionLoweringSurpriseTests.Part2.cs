using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed partial class ServerExtensionLoweringSurpriseTests
{
    [Fact]
    public async Task Server_extension_preserves_branch_assigned_string_local()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create("""
            using DotBoxD.Kernels;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            [ServerExtension("branch-string-local")]
            public sealed partial class BranchStringLocalKernel
            {
                public string Pick(bool flag, HookContext ctx)
                {
                    string value;
                    if (flag)
                    {
                        value = "left";
                    }
                    else
                    {
                        value = "right";
                    }

                    return value;
                }
            }
            """, "Sample.BranchStringLocalPluginPackage");

        using var server = PluginServer.Create(defaultPolicy: PurePolicy());
        var kernel = await server.InstallServerExtensionAsync(package);

        var whenTrue = await kernel.InvokeServerExtensionAsync([SandboxValue.FromBool(true)]);
        var whenFalse = await kernel.InvokeServerExtensionAsync([SandboxValue.FromBool(false)]);

        Assert.Equal("left", Assert.IsType<StringValue>(whenTrue).Value);
        Assert.Equal("right", Assert.IsType<StringValue>(whenFalse).Value);
    }

    [Fact]
    public async Task Server_extension_preserves_branch_assigned_list_local()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create("""
            using System.Collections.Generic;
            using DotBoxD.Kernels;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            [ServerExtension("branch-list-local")]
            public sealed partial class BranchListLocalKernel
            {
                public List<int> Pick(bool flag, HookContext ctx)
                {
                    List<int> value;
                    if (flag)
                    {
                        value = new List<int>();
                        value.Add(1);
                    }
                    else
                    {
                        value = new List<int>();
                        value.Add(2);
                        value.Add(3);
                    }

                    return value;
                }
            }
            """, "Sample.BranchListLocalPluginPackage");

        using var server = PluginServer.Create(defaultPolicy: PurePolicy());
        var kernel = await server.InstallServerExtensionAsync(package);

        var whenTrue = Assert.IsType<ListValue>(
            await kernel.InvokeServerExtensionAsync([SandboxValue.FromBool(true)]));
        var whenFalse = Assert.IsType<ListValue>(
            await kernel.InvokeServerExtensionAsync([SandboxValue.FromBool(false)]));

        Assert.Equal([1], whenTrue.Values.Select(item => ((I32Value)item).Value));
        Assert.Equal([2, 3], whenFalse.Values.Select(item => ((I32Value)item).Value));
    }
}
