using SafeIR;

namespace SafeIR.Tests;

public sealed class TypeAndLiteralValidationTests
{
    [Theory]
    [InlineData("F32")]
    [InlineData("Decimal")]
    [InlineData("Bytes")]
    [InlineData("Command")]
    public async Task Function_signatures_reject_unsupported_scalar_types(string typeName)
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(ModuleReturningType('"' + typeName + '"'));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-TYPE-UNKNOWN");
    }

    [Theory]
    [InlineData("""{ "name": "Option", "arguments": ["I32"] }""")]
    [InlineData("""{ "name": "Result", "arguments": ["I32", "String"] }""")]
    [InlineData("""{ "name": "Tuple", "arguments": ["I32", "String"] }""")]
    public async Task Function_signatures_reject_unsupported_composite_types(string typeJson)
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(ModuleReturningType(typeJson));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-TYPE-UNKNOWN");
    }

    [Fact]
    public async Task Function_signatures_reject_non_hashable_map_key_types()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(ModuleReturningType(
            """{ "name": "Map", "arguments": [{ "name": "List", "arguments": ["I32"] }, "I32"] }"""));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-TYPE-MAP-KEY");
    }

    [Theory]
    [InlineData("returnType", """
        {
          "id": "generic-string-type",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "List<String>",
              "body": [{ "op": "return", "value": { "i32": 0 } }]
            }
          ]
        }
        """)]
    [InlineData("parameter", """
        {
          "id": "generic-string-type",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "value", "type": "Map<String,I32>" }],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "i32": 0 } }]
            }
          ]
        }
        """)]
    [InlineData("call", """
        {
          "id": "generic-string-type",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": { "name": "List", "arguments": ["I32"] },
              "body": [{ "op": "return", "value": { "call": "list.empty", "genericType": "List<I32>" } }]
            }
          ]
        }
        """)]
    public void Generic_type_strings_are_rejected_at_json_import(string _, string json)
    {
        var ex = Assert.Throws<SandboxValidationException>(() => SafeIrJsonImporter.Import(json));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-JSON-TYPE");
    }

    [Theory]
    [InlineData("String", """{ "string": "System.IO.File.ReadAllText" }""")]
    [InlineData("String", """{ "string": "0x06000001" }""")]
    [InlineData("String", """{ "string": "IL_0001: calli" }""")]
    [InlineData("SandboxPath", """{ "path": "System.IO.File" }""")]
    [InlineData("SandboxUri", """{ "uri": "https://api.example.com/0x06000001" }""")]
    [InlineData("PlayerId", """{ "opaqueId": { "type": "PlayerId", "value": "0x06000001" } }""")]
    [InlineData("PlayerId", """{ "opaqueId": { "type": "PlayerId", "value": "System.Type" } }""")]
    public async Task Literals_reject_clr_and_il_payloads(string returnType, string literalJson)
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(ModuleReturning(returnType, literalJson));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(
                module,
                SandboxPolicyBuilder.Create().DeclareOpaqueIdType("PlayerId").Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-IR-CLR-REF");
    }

    private static string ModuleReturningType(string typeJson)
        => ModuleReturning(typeJson, """{ "i32": 0 }""", typeIsJson: true);

    private static string ModuleReturning(string returnType, string literalJson, bool typeIsJson = false)
    {
        var type = typeIsJson ? returnType : '"' + returnType + '"';
        return $$"""
        {
          "id": "type-and-literal-validation",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": {{type}},
              "body": [{ "op": "return", "value": {{literalJson}} }]
            }
          ]
        }
        """;
    }
}
