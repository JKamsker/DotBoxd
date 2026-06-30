using System.Text.Json;
using DotBoxD.Plugins.Json;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.SchemaDrift;

public sealed class Fix_CMP_0026_Tests
{
    [Fact]
    public void Plugin_package_schema_execution_mode_matches_case_insensitive_importer()
    {
        using var document = JsonDocument.Parse(PluginPackageJsonSchemas.PackageEnvelope);
        var mode = document.RootElement
            .GetProperty("$defs")
            .GetProperty("manifest")
            .GetProperty("properties")
            .GetProperty("mode");

        Assert.False(mode.TryGetProperty("enum", out _));
        Assert.Equal(
            "^(?:[Aa][Uu][Tt][Oo]|[Ii][Nn][Tt][Ee][Rr][Pp][Rr][Ee][Tt][Ee][Dd]|[Cc][Oo][Mm][Pp][Ii][Ll][Ee][Dd])$",
            mode.GetProperty("pattern").GetString());
    }

    [Fact]
    public void Plugin_package_schema_live_setting_values_match_declared_type()
    {
        using var document = JsonDocument.Parse(PluginPackageJsonSchemas.PackageEnvelope);
        var rules = document.RootElement
            .GetProperty("$defs")
            .GetProperty("liveSetting")
            .GetProperty("allOf")
            .EnumerateArray()
            .ToArray();

        Assert.Equal(5, rules.Length);
        Assert.Contains(rules, rule => MatchesLiveSettingType(rule, "bool", "boolean", "null", "null"));
        Assert.Contains(rules, rule => MatchesLiveSettingType(rule, "int", "integer", "null", "integer", "null"));
        Assert.Contains(rules, rule => MatchesLiveSettingType(rule, "long", "integer", "null", "integer", "null"));
        Assert.Contains(rules, rule => MatchesLiveSettingType(rule, "double", "number", "null", "number", "null"));
        Assert.Contains(rules, rule => MatchesLiveSettingType(rule, "string", "string", "null", "null"));
    }

    [Fact]
    public void Plugin_package_schema_indexed_predicate_values_match_declared_type()
    {
        using var document = JsonDocument.Parse(PluginPackageJsonSchemas.PackageEnvelope);
        var rules = document.RootElement
            .GetProperty("$defs")
            .GetProperty("indexedPredicate")
            .GetProperty("allOf")
            .EnumerateArray()
            .ToArray();

        Assert.Equal(5, rules.Length);
        Assert.Contains(rules, rule => MatchesIndexedPredicateType(rule, "bool", "boolean"));
        Assert.Contains(rules, rule => MatchesIndexedPredicateType(rule, "int", "integer", (decimal)int.MinValue, int.MaxValue));
        Assert.Contains(rules, rule => MatchesIndexedPredicateType(rule, "long", "integer", (decimal)long.MinValue, long.MaxValue));
        Assert.Contains(rules, rule => MatchesIndexedPredicateType(rule, "double", "number", -double.MaxValue, double.MaxValue));
        Assert.Contains(rules, rule => MatchesIndexedPredicateType(rule, "string", "string"));
    }

    [Fact]
    public void Plugin_package_schema_local_terminal_requires_projected_type()
    {
        using var document = JsonDocument.Parse(PluginPackageJsonSchemas.PackageEnvelope);
        var rules = document.RootElement
            .GetProperty("$defs")
            .GetProperty("subscription")
            .GetProperty("allOf")
            .EnumerateArray()
            .ToArray();

        Assert.Contains(rules, RequiresProjectedTypeForLocalTerminal);
    }

    [Fact]
    public void Drift_guard_rejects_same_property_set_when_required_properties_are_relaxed()
    {
        var contract = new JsonSchemaObjectContract(
            "plugin manifest",
            ["pluginId", "contract", "mode", "effects", "liveSettings", "subscriptions", "requiredCapabilities", "rpcEntrypoint"],
            ["pluginId", "contract", "mode", "effects", "liveSettings", "subscriptions"]);

        var failures = JsonSchemaDriftGuard.SemanticDriftMessages(
            """
            {
              "type": "object",
              "additionalProperties": false,
              "required": ["pluginId"],
              "properties": {
                "pluginId": { "type": "string" },
                "contract": { "type": "string" },
                "mode": { "type": "string" },
                "effects": { "type": "array" },
                "liveSettings": { "type": "array" },
                "subscriptions": { "type": "array" },
                "requiredCapabilities": { "type": "array" },
                "rpcEntrypoint": { "type": "string" }
              }
            }
            """,
            contract);

        Assert.Contains(failures, failure => failure.Contains("required", StringComparison.Ordinal));
    }

    [Fact]
    public void Drift_guard_rejects_same_property_set_when_statement_discriminator_const_drifts()
    {
        var contract = new JsonSchemaObjectContract(
            "expression statement",
            ["op", "value"],
            ["op", "value"])
        {
            ConstProperties = new Dictionary<string, string> { ["op"] = "expr" }
        };

        var failures = JsonSchemaDriftGuard.SemanticDriftMessages(
            """
            {
              "oneOf": [
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["op", "value"],
                  "properties": {
                    "op": { "const": "return" },
                    "value": { "$ref": "#/$defs/expression" }
                  }
                }
              ]
            }
            """,
            contract);

        Assert.Contains(failures, failure => failure.Contains("const", StringComparison.Ordinal));
    }

    private static bool MatchesLiveSettingType(
        JsonElement rule,
        string type,
        params string[] expectedTypes)
        => ConditionType(rule, type) &&
           PropertyTypes(rule, "defaultValue").SequenceEqual(expectedTypes.Take(2)) &&
           PropertyTypes(rule, "min").SequenceEqual(expectedTypes.Skip(2)) &&
           PropertyTypes(rule, "max").SequenceEqual(expectedTypes.Skip(2));

    private static bool ConditionType(JsonElement rule, string type)
        => rule.GetProperty("if")
            .GetProperty("properties")
            .GetProperty("type")
            .GetProperty("const")
            .GetString() == type;

    private static IReadOnlyList<string> PropertyTypes(JsonElement rule, string propertyName)
    {
        var type = rule.GetProperty("then")
            .GetProperty("properties")
            .GetProperty(propertyName)
            .GetProperty("type");
        return type.ValueKind == JsonValueKind.Array
            ? type.EnumerateArray().Select(item => item.GetString()!).ToArray()
            : [type.GetString()!];
    }

    private static bool MatchesIndexedPredicateType(
        JsonElement rule,
        string valueType,
        string expectedType)
        => ConditionValueType(rule, valueType) &&
           PropertyTypes(rule, "value").SequenceEqual([expectedType]) &&
           !rule.GetProperty("then").GetProperty("properties").GetProperty("value").TryGetProperty("minimum", out _) &&
           !rule.GetProperty("then").GetProperty("properties").GetProperty("value").TryGetProperty("maximum", out _);

    private static bool MatchesIndexedPredicateType(
        JsonElement rule,
        string valueType,
        string expectedType,
        decimal expectedMinimum,
        decimal expectedMaximum)
        => ConditionValueType(rule, valueType) &&
           PropertyTypes(rule, "value").SequenceEqual([expectedType]) &&
           rule.GetProperty("then").GetProperty("properties").GetProperty("value").GetProperty("minimum").GetDecimal() == expectedMinimum &&
           rule.GetProperty("then").GetProperty("properties").GetProperty("value").GetProperty("maximum").GetDecimal() == expectedMaximum;

    private static bool MatchesIndexedPredicateType(
        JsonElement rule,
        string valueType,
        string expectedType,
        double expectedMinimum,
        double expectedMaximum)
        => ConditionValueType(rule, valueType) &&
           PropertyTypes(rule, "value").SequenceEqual([expectedType]) &&
           rule.GetProperty("then").GetProperty("properties").GetProperty("value").GetProperty("minimum").GetDouble() == expectedMinimum &&
           rule.GetProperty("then").GetProperty("properties").GetProperty("value").GetProperty("maximum").GetDouble() == expectedMaximum;

    private static bool ConditionValueType(JsonElement rule, string valueType)
        => rule.GetProperty("if")
            .GetProperty("properties")
            .GetProperty("valueType")
            .GetProperty("const")
            .GetString() == valueType;

    private static bool RequiresProjectedTypeForLocalTerminal(JsonElement rule)
        => rule.GetProperty("if").TryGetProperty("properties", out var properties) &&
           properties.TryGetProperty("localTerminal", out var localTerminal) &&
           localTerminal.TryGetProperty("const", out var constValue) &&
           constValue.ValueKind == JsonValueKind.True &&
           rule.GetProperty("then").GetProperty("required")
               .EnumerateArray()
               .Any(value => value.GetString() == "projectedType");
}
