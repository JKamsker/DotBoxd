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

    [Fact]
    public void Plugin_package_schema_declares_result_metadata_properties()
    {
        var properties = SubscriptionProperties();

        Assert.Equal("string", properties.GetProperty("resultType").GetProperty("type").GetString());
        Assert.Equal("boolean", properties.GetProperty("resultLocalTerminal").GetProperty("type").GetString());
    }

    [Fact]
    public void Plugin_package_schema_result_local_terminal_requires_result_type()
    {
        var rules = SubscriptionRules();
        var rule = Assert.Single(rules.EnumerateArray(), RuleMentionsResultLocalTerminal);

        Assert.Equal("resultType", Assert.Single(rule.GetProperty("then").GetProperty("required").EnumerateArray()).GetString());
    }

    [Fact]
    public void Plugin_package_schema_result_type_rejects_run_local_projection_metadata()
    {
        var rules = SubscriptionRules();
        var rule = Assert.Single(rules.EnumerateArray(), RuleRequiresResultType);
        var invalidCombinations = rule.GetProperty("then").GetProperty("not").GetProperty("anyOf");

        Assert.Contains(invalidCombinations.EnumerateArray(), RequiresLocalTerminalConstTrue);
        Assert.Contains(invalidCombinations.EnumerateArray(), item =>
            item.TryGetProperty("required", out var required) &&
            required.EnumerateArray().Any(value => value.GetString() == "projectedType"));
    }

    [Fact]
    public void Plugin_package_schema_projected_type_requires_local_terminal()
    {
        var rules = SubscriptionRules();
        var rule = Assert.Single(rules.EnumerateArray(), RuleRequiresProjectedType);

        Assert.True(rule.GetProperty("then").GetProperty("properties").GetProperty("localTerminal").GetProperty("const").GetBoolean());
        Assert.Equal(
            "localTerminal",
            Assert.Single(rule.GetProperty("then").GetProperty("required").EnumerateArray()).GetString());
    }

    private static JsonElement SubscriptionProperties()
    {
        using var document = JsonDocument.Parse(PluginPackageJsonSchemas.PackageEnvelope);
        return document.RootElement
            .GetProperty("$defs")
            .GetProperty("subscription")
            .GetProperty("properties")
            .Clone();
    }

    private static JsonElement SubscriptionRules()
    {
        using var document = JsonDocument.Parse(PluginPackageJsonSchemas.PackageEnvelope);
        return document.RootElement
            .GetProperty("$defs")
            .GetProperty("subscription")
            .GetProperty("allOf")
            .Clone();
    }

    private static bool RuleMentionsResultLocalTerminal(JsonElement rule)
        => rule.GetProperty("if").TryGetProperty("properties", out var properties) &&
           properties.TryGetProperty("resultLocalTerminal", out _);

    private static bool RuleRequiresResultType(JsonElement rule)
        => rule.GetProperty("if").TryGetProperty("required", out var required) &&
           required.EnumerateArray().Any(value => value.GetString() == "resultType");

    private static bool RuleRequiresProjectedType(JsonElement rule)
        => rule.GetProperty("if").TryGetProperty("required", out var required) &&
           required.EnumerateArray().Any(value => value.GetString() == "projectedType");

    private static bool RequiresLocalTerminalConstTrue(JsonElement item)
        => item.TryGetProperty("required", out var required) &&
           required.EnumerateArray().Any(value => value.GetString() == "localTerminal") &&
           item.TryGetProperty("properties", out var properties) &&
           properties.TryGetProperty("localTerminal", out var localTerminal) &&
           localTerminal.TryGetProperty("const", out var constValue) &&
           constValue.ValueKind == JsonValueKind.True;
}
