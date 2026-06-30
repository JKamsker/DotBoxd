using System.Text.Json;

namespace DotBoxD.Kernels.Tests.Compiled.Regression;

public sealed partial class Fix_CMP_0012_Tests
{
    [Fact]
    public void Module_schema_type_string_domain_matches_importer()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RepositoryPath(ModuleSchemaRelative)));
        var stringType = document.RootElement
            .GetProperty("$defs")
            .GetProperty("type")
            .GetProperty("oneOf")
            .EnumerateArray()
            .Single(branch =>
                branch.TryGetProperty("type", out var type) &&
                type.GetString() == "string");

        Assert.True(
            stringType.TryGetProperty("not", out var not),
            "type string schema must reject generic type strings.");
        AssertPattern(not, "[<>]");
    }
}
