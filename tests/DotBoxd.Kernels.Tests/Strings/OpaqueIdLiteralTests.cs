namespace DotBoxd.Kernels.Tests;

public sealed class OpaqueIdLiteralTests
{
    private static readonly string[] TestOpaqueIdTypes = ["PlayerId", "ItemId", "QuestId", "MapId"];

    [Fact]
    public async Task Json_unit_literal_executes()
    {
        var result = await ExecuteAsync("Unit", """{ "unit": true }""");

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Same(SandboxValue.Unit, result.Value);
    }

    [Theory]
    [InlineData("PlayerId")]
    [InlineData("ItemId")]
    [InlineData("QuestId")]
    [InlineData("MapId")]
    public async Task Json_opaque_id_literals_execute_as_declared_scalar_type(string typeName)
    {
        var result = await ExecuteAsync(
            typeName,
            $$"""{ "opaqueId": { "type": "{{typeName}}", "value": "tenant:alpha_01" } }""");

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        var value = Assert.IsType<OpaqueIdValue>(result.Value);
        Assert.Equal(typeName, value.TypeName);
        Assert.Equal("tenant:alpha_01", value.Value);
    }

    [Fact]
    public async Task Opaque_id_literal_can_be_used_as_map_key()
    {
        var host = SandboxTestHost.Create(compiler: false);
        var module = await host.ImportJsonAsync("""
        {
          "id": "opaque-id-map-key",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Bool",
              "body": [
                {
                  "op": "return",
                  "value": {
                    "call": "map.containsKey",
                    "args": [
                      {
                        "call": "map.set",
                        "args": [
                          {
                            "call": "map.empty",
                            "genericType": {
                              "name": "Map",
                              "arguments": ["PlayerId", "I32"]
                            }
                          },
                          { "opaqueId": { "type": "PlayerId", "value": "player:1" } },
                          { "i32": 7 }
                        ]
                      },
                      { "opaqueId": { "type": "PlayerId", "value": "player:1" } }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """);
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create().DeclareOpaqueIdType("PlayerId").WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.True(((BoolValue)result.Value!).Value);
    }

    [Fact]
    public void Opaque_id_literal_rejects_unsafe_characters()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => DotBoxdJsonImporter.Import(Module(
            "PlayerId",
            """{ "opaqueId": { "type": "PlayerId", "value": "../secret" } }""")));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-JSON-ID");
    }

    private static async Task<SandboxExecutionResult> ExecuteAsync(string returnType, string literalJson)
    {
        var host = SandboxTestHost.Create(compiler: false);
        var module = await host.ImportJsonAsync(Module(returnType, literalJson));
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create().DeclareOpaqueIdTypes(TestOpaqueIdTypes).WithFuel(1_000).Build());
        return await host.ExecuteAsync(plan, "main", SandboxValue.Unit);
    }

    private static string Module(string returnType, string literalJson)
        => $$"""
        {
          "id": "opaque-id-literals",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "{{returnType}}",
              "body": [{ "op": "return", "value": {{literalJson}} }]
            }
          ]
        }
        """;
}
