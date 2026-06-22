using System.Text.Json;
using DotBoxD.Plugins.Json;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.SchemaDrift;

public sealed class PluginPackageSchemaResultMetadataTests
{
    [Fact]
    public void Plugin_package_schema_priority_matches_importer_int32_range()
    {
        using var document = JsonDocument.Parse(PluginPackageJsonSchemas.PackageEnvelope);
        var priority = document.RootElement
            .GetProperty("$defs")
            .GetProperty("subscription")
            .GetProperty("properties")
            .GetProperty("priority");

        Assert.Equal(int.MinValue, priority.GetProperty("minimum").GetInt32());
        Assert.Equal(int.MaxValue, priority.GetProperty("maximum").GetInt32());
    }
}
