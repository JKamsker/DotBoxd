using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionUnsupportedFrameworkStructTests
{
    [Fact]
    public void Server_extension_rejects_unsupported_framework_struct_parameters()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using System;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            [ServerExtension("date-value")]
            public sealed partial class DateValueKernel
            {
                public int UseDate(DateOnly value, HookContext ctx) => 0;
            }

            [ServerExtension("time-value")]
            public sealed partial class TimeValueKernel
            {
                public int UseTime(TimeOnly value, HookContext ctx) => 0;
            }

            [ServerExtension("index-value")]
            public sealed partial class IndexValueKernel
            {
                public int UseIndex(Index value, HookContext ctx) => 0;
            }

            [ServerExtension("range-value")]
            public sealed partial class RangeValueKernel
            {
                public int UseRange(Range value, HookContext ctx) => 0;
            }
            """);

        AssertUnsupported(diagnostics, "System.DateOnly");
        AssertUnsupported(diagnostics, "System.TimeOnly");
        AssertUnsupported(diagnostics, "System.Index");
        AssertUnsupported(diagnostics, "System.Range");
    }

    private static void AssertUnsupported(IEnumerable<Microsoft.CodeAnalysis.Diagnostic> diagnostics, string typeName)
        => Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" && d.GetMessage().Contains(typeName, StringComparison.Ordinal));
}
