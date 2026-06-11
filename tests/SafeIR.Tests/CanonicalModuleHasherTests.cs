using System.Globalization;

namespace SafeIR.Tests;

public sealed class CanonicalModuleHasherTests
{
    [Fact]
    public void Floating_point_literals_hash_independently_of_current_culture()
    {
        var module = SafeIrJsonImporter.Import(ModuleWithReturn("""{ "f64": 1.5 }""", "F64"));
        var originalCulture = CultureInfo.CurrentCulture;
        try {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var germanHash = CanonicalModuleHasher.Hash(module);

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var englishHash = CanonicalModuleHasher.Hash(module);
            var serialized = CanonicalModuleHasher.Serialize(module);

            Assert.Equal(germanHash, englishHash);
            Assert.Contains("f64:1.5", serialized);
            Assert.DoesNotContain("f64:1,5", serialized);
        }
        finally {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void Canonical_serialization_escapes_internal_separators()
    {
        var module = SafeIrJsonImporter.Import(ModuleWithReturn(
            """{ "string": "a\u001fb\r\nc\\d" }""",
            "String"));

        var serialized = CanonicalModuleHasher.Serialize(module);

        Assert.Contains("string:a\\u001fb\\r\\nc\\\\d", serialized);
    }

    private static string ModuleWithReturn(string expression, string returnType)
        => $$"""
        {
          "id": "canonical-literals",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "{{returnType}}",
              "body": [{ "op": "return", "value": {{expression}} }]
            }
          ]
        }
        """;
}
