using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.PluginIpc.Server.Abstractions;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins.Json;

namespace DotBoxD.Kernels.Tests.Plugins;

public sealed class PluginPackageJsonTests
{
    [Fact]
    public async Task InstallJsonAsync_installs_serialized_package_data()
    {
        var messages = new InMemoryPluginMessageSink();
        var server = DotBoxD.Plugins.PluginServer.Create(messages, defaultPolicy: PluginAddendumTestPolicies.LongWall());

        var kernel = await server.InstallJsonAsync(JsonDamagePackage());
        server.Hooks.On(DamageEventAdapter.Instance).Use(kernel);

        await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-1"));

        var message = Assert.Single(messages.Messages);
        Assert.Equal("player-1", message.TargetId);
        Assert.Equal("json package handled damage", message.Message);
    }

    [Fact]
    public async Task InstallJsonAsync_default_policy_denies_game_message_write()
    {
        var server = DotBoxD.Plugins.PluginServer.Create(new InMemoryPluginMessageSink());

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await server.InstallJsonAsync(JsonDamagePackage()).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code is "E-POLICY-CAP" or "E-POLICY-EFFECT");
    }

    [Fact]
    public void Import_rejects_dll_shaped_package_boundary()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => PluginPackageJsonSerializer.Import("""
        {
          "assemblyPath": "plugin.dll"
        }
        """));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-JSON-SCHEMA");
    }

    [Fact]
    public void Import_rejects_manifest_module_mismatch_before_returning_package()
    {
        var json = JsonDamagePackage().Replace(
            "\"id\": \"json-fire-damage\"",
            "\"id\": \"other-plugin\"",
            StringComparison.Ordinal);

        var ex = Assert.Throws<SandboxValidationException>(() => PluginPackageJsonSerializer.Import(json));

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK011");
    }

    [Fact]
    public void Import_rejects_required_capabilities_that_are_not_an_array()
    {
        var json = JsonDamagePackage().Replace(
            "\"requiredCapabilities\": [\"dotboxd.runtime.async\", \"host.message.write\"]",
            "\"requiredCapabilities\": \"host.message.write\"",
            StringComparison.Ordinal);

        var ex = Assert.Throws<SandboxValidationException>(() => PluginPackageJsonSerializer.Import(json));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-JSON-TYPE");
    }

    [Fact]
    public void Import_rejects_projected_type_that_is_not_a_string()
    {
        var json = JsonDamagePackage().Replace(
            """{ "event": "DamageEvent", "kernel": "JsonDamageKernel" }""",
            """{ "event": "DamageEvent", "kernel": "JsonDamageKernel", "localTerminal": true, "projectedType": 123 }""",
            StringComparison.Ordinal);

        var ex = Assert.Throws<SandboxValidationException>(() => PluginPackageJsonSerializer.Import(json));

        Assert.Contains(ex.Diagnostics, d =>
            d.Code == "E-JSON-TYPE" &&
            d.Message.Contains("'projectedType' must be a string", StringComparison.Ordinal));
    }

    [Fact]
    public void Import_rejects_projected_type_without_local_terminal()
    {
        var json = JsonDamagePackage().Replace(
            """{ "event": "DamageEvent", "kernel": "JsonDamageKernel" }""",
            """{ "event": "DamageEvent", "kernel": "JsonDamageKernel", "projectedType": "string" }""",
            StringComparison.Ordinal);

        var ex = Assert.Throws<SandboxValidationException>(() => PluginPackageJsonSerializer.Import(json));

        Assert.Contains(ex.Diagnostics, d =>
            d.Code == "DBXK031" &&
            d.Message.Contains("projectedType", StringComparison.Ordinal) &&
            d.Message.Contains("localTerminal", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("3")]
    [InlineData("Cpu, Alloc")]
    public void Import_rejects_effect_strings_outside_schema_enum(string effect)
    {
        var json = JsonDamagePackage().Replace(
            "\"effects\": [\"Cpu\", \"Alloc\", \"HostStateWrite\", \"Concurrency\", \"Audit\"]",
            $"\"effects\": [\"{effect}\"]",
            StringComparison.Ordinal);

        var ex = Assert.Throws<SandboxValidationException>(() => PluginPackageJsonSerializer.Import(json));

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK040");
    }

    [Fact]
    public void Import_rejects_oversized_package_json_before_dom_parse()
    {
        var oversized = "{\"manifest\":\"" + new string('x', 1_048_577) + "\"}";

        var ex = Assert.Throws<SandboxValidationException>(() => PluginPackageJsonSerializer.Import(oversized));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-JSON-LIMIT");
    }

    [Theory]
    [InlineData("\"pluginId\": \"json-fire-damage\"", "\"pluginId\": \"other\"")]
    [InlineData("\"name\": \"DamageType\"", "\"name\": \"Other\"")]
    [InlineData("\"event\": \"DamageEvent\"", "\"event\": \"OtherEvent\"")]
    public void Import_rejects_duplicate_plugin_manifest_shapes(string first, string duplicate)
    {
        var json = JsonDamagePackage("""
            { "name": "DamageType", "name": "Other", "type": "string", "defaultValue": "fire" }
            """);
        if (first != "\"name\": \"DamageType\"")
        {
            json = JsonDamagePackage().Replace(first, first + ", " + duplicate, StringComparison.Ordinal);
        }

        var ex = Assert.Throws<SandboxValidationException>(() => PluginPackageJsonSerializer.Import(json));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-JSON-SCHEMA");
    }

    [Theory]
    [InlineData("Acme.Plugin.Kernel, Acme.Plugin, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null")]
    [InlineData("RuntimeTypeHandle")]
    [InlineData("MetadataToken=06000001")]
    public async Task InstallJsonAsync_rejects_manifest_loader_descriptors(string contract)
    {
        var server = DotBoxD.Plugins.PluginServer.Create();
        var json = JsonDamagePackage().Replace(
            "\"contract\": \"IEventKernel<DamageEvent>\"",
            $"\"contract\": \"{contract}\"",
            StringComparison.Ordinal);

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await server.InstallJsonAsync(json).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK050");
    }

    [Fact]
    public async Task InstallJsonAsync_rejects_module_metadata_loader_descriptors()
    {
        var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var json = JsonDamagePackage().Replace(
            "\"metadata\": { \"pluginId\": \"json-fire-damage\", \"kernel\": \"JsonDamageKernel\" }",
            "\"metadata\": { \"pluginId\": \"json-fire-damage\", \"kernel\": \"JsonDamageKernel\", \"rawIlBase64\": \"AAAA\" }",
            StringComparison.Ordinal);

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await server.InstallJsonAsync(json).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-IR-CLR-REF");
    }

    [Theory]
    [InlineData("assemblyPath", "plugin.dll")]
    [InlineData("rawDllBase64", "AAAA")]
    [InlineData("plugin.dll", "loader")]
    public async Task InstallJsonAsync_rejects_module_metadata_dll_loader_hints(string key, string value)
    {
        var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var json = JsonDamagePackage().Replace(
            "\"metadata\": { \"pluginId\": \"json-fire-damage\", \"kernel\": \"JsonDamageKernel\" }",
            $"\"metadata\": {{ \"pluginId\": \"json-fire-damage\", \"kernel\": \"JsonDamageKernel\", \"{key}\": \"{value}\" }}",
            StringComparison.Ordinal);

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await server.InstallJsonAsync(json).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-IR-CLR-REF");
    }

    [Fact]
    public void Import_converts_live_setting_defaults_to_clr_scalars()
    {
        var package = PluginPackageJsonSerializer.Import(JsonDamagePackageWithSettings());

        Assert.Collection(
            package.Manifest.LiveSettings,
            setting =>
            {
                Assert.Equal("DamageType", setting.Name);
                Assert.IsType<string>(setting.DefaultValue);
                Assert.Equal("fire", setting.DefaultValue);
            },
            setting =>
            {
                Assert.Equal("MinDamage", setting.Name);
                Assert.IsType<int>(setting.DefaultValue);
                Assert.Equal(100, setting.DefaultValue);
                Assert.IsType<int>(setting.Min);
                Assert.IsType<int>(setting.Max);
            });
    }

    [Fact]
    public void Import_rejects_live_setting_default_outside_manifest_range()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => PluginPackageJsonSerializer.Import(JsonDamagePackage(
            """{ "name": "MinDamage", "type": "int", "defaultValue": 10001, "min": 0, "max": 10000 }""")));
        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK023");
    }

    private static string JsonDamagePackageWithSettings()
        => JsonDamagePackage("""
            { "name": "DamageType", "type": "string", "defaultValue": "fire" },
            { "name": "MinDamage", "type": "int", "defaultValue": 100, "min": 0, "max": 10000 }
            """);

    private static string JsonDamagePackage(string liveSettings = "")
        => $$"""
        {
          "manifest": {
            "pluginId": "json-fire-damage",
            "contract": "IEventKernel<DamageEvent>",
            "mode": "Interpreted",
            "effects": ["Cpu", "Alloc", "HostStateWrite", "Concurrency", "Audit"],
            "liveSettings": [{{liveSettings}}],
            "subscriptions": [
              { "event": "DamageEvent", "kernel": "JsonDamageKernel" }
            ],
            "requiredCapabilities": ["dotboxd.runtime.async", "host.message.write"]
          },
          "module": {
            "id": "json-fire-damage",
            "version": "1.0.0",
            "targetSandboxVersion": "1.0.0",
            "capabilityRequests": [
              { "id": "host.message.write", "reason": "send host messages" }
            ],
            "metadata": { "pluginId": "json-fire-damage", "kernel": "JsonDamageKernel" },
            "functions": [
              {
                "id": "ShouldHandle",
                "visibility": "entrypoint",
                "parameters": [
                  { "name": "e_DamageType", "type": "String" },
                  { "name": "e_Amount", "type": "I32" },
                  { "name": "e_TargetId", "type": "String" }
                ],
                "returnType": "Bool",
                "body": [
                  { "op": "return", "value": { "bool": true } }
                ]
              },
              {
                "id": "Handle",
                "visibility": "entrypoint",
                "parameters": [
                  { "name": "e_DamageType", "type": "String" },
                  { "name": "e_Amount", "type": "I32" },
                  { "name": "e_TargetId", "type": "String" }
                ],
                "returnType": "Unit",
                "body": [
                  {
                    "op": "return",
                    "value": {
                      "call": "host.message.send",
                      "args": [
                        { "var": "e_TargetId" },
                        { "string": "json package handled damage" }
                      ]
                    }
                  }
                ]
              }
            ]
          }
        }
        """;
}
