using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed partial class RpcKernelGenerationTests
{
    [Fact]
    public async Task Ulong_enum_kernel_method_default_lowers_as_bit_preserving_i64()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create("""
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;

            namespace Sample;

            public enum Huge : ulong
            {
                Top = ulong.MaxValue
            }

            [ServerExtension("huge-enum")]
            public sealed partial class HugeEnumKernel
            {
                public Huge Read(HookContext ctx)
                {
                    return Pick();
                }

                [KernelMethod]
                public static Huge Pick(Huge value = Huge.Top) => value;
            }
            """, "Sample.HugeEnumPluginPackage");

        using var server = PluginServer.Create(defaultPolicy: PurePolicy());
        var kernel = await server.InstallServerExtensionAsync(package);

        var result = await kernel.InvokeServerExtensionAsync([]);

        Assert.Equal(SandboxValue.FromInt64(-1), result);
    }

    [Fact]
    public async Task Ulong_enum_constant_lowers_as_bit_preserving_i64()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create("""
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels;
            using DotBoxD.Plugins;

            namespace Sample;

            public enum Huge : ulong
            {
                Top = ulong.MaxValue
            }

            [ServerExtension("huge-enum-constant")]
            public sealed partial class HugeEnumConstantKernel
            {
                public Huge Read(HookContext ctx)
                {
                    return Huge.Top;
                }
            }
            """, "Sample.HugeEnumConstantPluginPackage");

        using var server = PluginServer.Create(defaultPolicy: PurePolicy());
        var kernel = await server.InstallServerExtensionAsync(package);

        var result = await kernel.InvokeServerExtensionAsync([]);

        Assert.Equal(SandboxValue.FromInt64(-1), result);
    }

    [Fact]
    public async Task Server_extension_lowers_string_concatenation_to_budgeted_concat()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create("""
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;

            namespace Sample;

            [ServerExtension("string-concat")]
            public sealed partial class StringConcatKernel
            {
                public string Combine(string left, string right, HookContext ctx)
                {
                    return left + ":" + right;
                }
            }
            """, "Sample.StringConcatPluginPackage");

        Assert.Contains("Alloc", package.Manifest.Effects);

        var returned = Assert.IsType<ReturnStatement>(Assert.Single(Assert.Single(package.Module.Functions).Body));
        var concat = Assert.IsType<CallExpression>(returned.Value);
        Assert.Equal("string.concatBudgeted", concat.Name);

        using var server = PluginServer.Create(defaultPolicy: PurePolicy());
        var kernel = await server.InstallServerExtensionAsync(package);

        var result = await kernel.InvokeServerExtensionAsync(
            [SandboxValue.FromString("left"), SandboxValue.FromString("right")]);

        Assert.Equal("left:right", Assert.IsType<StringValue>(result).Value);
    }

    [Fact]
    public async Task Server_extension_lowers_string_add_assignment_to_budgeted_concat()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create("""
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;

            namespace Sample;

            [ServerExtension("string-add-assign")]
            public sealed partial class StringAddAssignKernel
            {
                public string Combine(string left, string right, HookContext ctx)
                {
                    var combined = left;
                    combined += ":";
                    combined += right;
                    return combined;
                }
            }
            """, "Sample.StringAddAssignPluginPackage");

        Assert.Contains("Alloc", package.Manifest.Effects);

        using var server = PluginServer.Create(defaultPolicy: PurePolicy());
        var kernel = await server.InstallServerExtensionAsync(package);

        var result = await kernel.InvokeServerExtensionAsync(
            [SandboxValue.FromString("left"), SandboxValue.FromString("right")]);

        Assert.Equal("left:right", Assert.IsType<StringValue>(result).Value);
    }

    [Theory]
    [InlineData("double.NaN")]
    [InlineData("double.PositiveInfinity")]
    [InlineData("double.NegativeInfinity")]
    public void Non_finite_double_literals_are_rejected_by_rpc_analyzer(string literal)
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics($$"""
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            [ServerExtension("bad-f64")]
            public sealed partial class BadF64Kernel
            {
                public double Read(HookContext ctx)
                {
                    return {{literal}};
                }
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("finite", StringComparison.OrdinalIgnoreCase));
    }
}
