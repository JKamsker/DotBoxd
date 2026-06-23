using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class RpcKernelMemberAccessValidationTests
{
    [Fact]
    public void Kernel_rpc_service_rejects_class_field_identifier_reads()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            [ServerExtension("field-read")]
            public sealed partial class FieldReadKernel
            {
                private readonly int _offset = 1;

                public int Read(int value, HookContext ctx)
                {
                    return _offset;
                }
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("identifier '_offset' is not a local or parameter", StringComparison.Ordinal));
    }
}
