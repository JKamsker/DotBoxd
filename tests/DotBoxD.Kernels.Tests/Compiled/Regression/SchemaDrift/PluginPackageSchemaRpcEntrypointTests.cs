using System.Text.Json;
using DotBoxD.Plugins.Json;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.SchemaDrift;

public sealed class PluginPackageSchemaRpcEntrypointTests
{
    [Fact]
    public void Plugin_package_schema_requires_entrypoint_aliases_when_rpc_entrypoint_is_present()
    {
        using var document = JsonDocument.Parse(PluginPackageJsonSchemas.PackageEnvelope);
        var root = document.RootElement;

        Assert.True(root.TryGetProperty("allOf", out var rules), "Plugin package schema must declare conditional rules.");
        var rule = Assert.Single(rules.EnumerateArray(), RuleTestsRpcEntrypoint);

        Assert.Contains(
            rule.GetProperty("then").GetProperty("required").EnumerateArray(),
            item => item.GetString() == "entrypoints");

        var entrypointRequirements = rule.GetProperty("then")
            .GetProperty("properties")
            .GetProperty("entrypoints")
            .GetProperty("required")
            .EnumerateArray()
            .Select(item => item.GetString()!)
            .ToArray();
        Assert.Equal(["shouldHandle", "handle"], entrypointRequirements);
    }

    private static bool RuleTestsRpcEntrypoint(JsonElement rule)
        => rule.TryGetProperty("if", out var condition) &&
           condition.TryGetProperty("properties", out var properties) &&
           properties.TryGetProperty("manifest", out var manifest) &&
           manifest.TryGetProperty("required", out var required) &&
           required.EnumerateArray().Any(value => value.GetString() == "rpcEntrypoint");
}
