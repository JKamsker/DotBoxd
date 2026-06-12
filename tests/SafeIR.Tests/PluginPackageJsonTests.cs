using SafeIR.PluginIpc.Server.Abstractions;
using SafeIR.Plugins;

namespace SafeIR.Tests;

public sealed class PluginPackageJsonTests
{
    [Fact]
    public async Task InstallJsonAsync_installs_serialized_package_data()
    {
        var messages = new InMemoryPluginMessageSink();
        var server = PluginServer.Create(messages);

        var kernel = await server.InstallJsonAsync(JsonDamagePackage());
        server.Hooks.On(DamageEventAdapter.Instance).UseKernel(kernel);

        await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-1"));

        var message = Assert.Single(messages.Messages);
        Assert.Equal("player-1", message.TargetId);
        Assert.Equal("json package handled damage", message.Message);
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
    public void Import_rejects_oversized_package_json_before_dom_parse()
    {
        var oversized = "{\"manifest\":\"" + new string('x', 1_048_577) + "\"}";

        var ex = Assert.Throws<SandboxValidationException>(() => PluginPackageJsonSerializer.Import(oversized));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-JSON-LIMIT");
    }

    [Theory]
    [InlineData("Acme.Plugin.Kernel, Acme.Plugin, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null")]
    [InlineData("RuntimeTypeHandle")]
    [InlineData("MetadataToken=06000001")]
    public async Task InstallJsonAsync_rejects_manifest_loader_descriptors(string contract)
    {
        var server = PluginServer.Create();
        var json = JsonDamagePackage().Replace(
            "\"contract\": \"IEventKernel<DamageEvent>\"",
            $"\"contract\": \"{contract}\"",
            StringComparison.Ordinal);

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await server.InstallJsonAsync(json).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "SGP050");
    }

    [Fact]
    public async Task InstallJsonAsync_rejects_module_metadata_loader_descriptors()
    {
        var server = PluginServer.Create();
        var json = JsonDamagePackage().Replace(
            "\"metadata\": { \"pluginId\": \"json-fire-damage\", \"kernel\": \"JsonDamageKernel\" }",
            "\"metadata\": { \"pluginId\": \"json-fire-damage\", \"kernel\": \"JsonDamageKernel\", \"rawIlBase64\": \"AAAA\" }",
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
            "effects": ["Cpu", "Alloc", "GameStateWrite", "Audit"],
            "liveSettings": [{{liveSettings}}],
            "subscriptions": [
              { "event": "DamageEvent", "kernel": "JsonDamageKernel" }
            ]
          },
          "module": {
            "id": "json-fire-damage",
            "version": "1.0.0",
            "targetSandboxVersion": "1.0.0",
            "capabilityRequests": [
              { "id": "game.message.write", "reason": "send damage notifications" }
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
                      "call": "game.message.send",
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
