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

    [Fact]
    public void Module_schema_path_and_uri_literals_expose_importer_lexical_constraints()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RepositoryPath(ModuleSchemaRelative)));
        var literalProperties = document.RootElement
            .GetProperty("$defs")
            .GetProperty("literal")
            .GetProperty("properties");

        Assert.True(
            literalProperties.GetProperty("path").TryGetProperty("pattern", out _),
            "path literal schema must expose the portable relative path constraint.");
        Assert.True(
            literalProperties.GetProperty("uri").TryGetProperty("pattern", out _),
            "uri literal schema must expose the absolute sandbox URI constraint.");
    }
}
