using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

/// <summary>
/// A collection type that is not one of the recognized list/map shapes (e.g. <c>ImmutableArray&lt;T&gt;</c>,
/// which exposes only scalar getters such as <c>Length</c>) must be rejected with a diagnostic rather than
/// silently marshalled as a metadata-only record that drops the element data (issue #7). A plain record DTO
/// with scalar fields must still marshal as a record (no over-exclusion).
/// </summary>
public sealed class ServerExtensionCollectionParameterTests
{
    private const string ImmutableArrayParameterSource = """
        using System.Collections.Immutable;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        [ServerExtension("immutable-array")]
        public sealed partial class ImmutableArrayKernel
        {
            public int Use(ImmutableArray<int> data, HookContext ctx)
            {
                return 0;
            }
        }
        """;

    private const string PlainRecordParameterSource = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        public sealed record Point(int X, int Y);

        [ServerExtension("plain-record")]
        public sealed partial class PlainRecordKernel
        {
            public int Use(Point point, HookContext ctx)
            {
                return point.X;
            }
        }
        """;

    [Fact]
    public void Server_extension_rejects_a_collection_that_is_not_a_recognized_list()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(ImmutableArrayParameterSource);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" && d.GetMessage().Contains("is not supported", StringComparison.Ordinal));
    }

    [Fact]
    public void Server_extension_still_marshals_a_plain_record_dto_parameter()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            PlainRecordParameterSource,
            "Sample.PlainRecordPluginPackage");

        var function = Assert.Single(package.Module.Functions);
        var parameter = Assert.Single(function.Parameters);
        Assert.Equal(SandboxType.Record([SandboxType.I32, SandboxType.I32]), parameter.Type);
    }
}
