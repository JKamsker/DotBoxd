using SafeIR;

namespace SafeIR.Tests;

public sealed class CompiledMapCollectionTests
{
    [Fact]
    public async Task Compiled_map_operations_match_interpreter()
    {
        const string moduleJson = """
        {
          "id": "compiled-map",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                {
                  "op": "set",
                  "name": "scores",
                  "value": {
                    "call": "map.set",
                    "args": [
                      {
                        "call": "map.empty",
                        "genericType": { "name": "Map", "arguments": ["String", "I32"] },
                        "args": []
                      },
                      { "string": "alice" },
                      { "i32": 41 }
                    ]
                  }
                },
                {
                  "op": "set",
                  "name": "trimmed",
                  "value": {
                    "call": "map.remove",
                    "args": [{ "var": "scores" }, { "string": "bob" }]
                  }
                },
                {
                  "op": "if",
                  "condition": {
                    "call": "map.containsKey",
                    "args": [{ "var": "trimmed" }, { "string": "alice" }]
                  },
                  "then": [
                    {
                      "op": "return",
                      "value": {
                        "call": "map.get",
                        "args": [{ "var": "trimmed" }, { "string": "alice" }]
                      }
                    }
                  ],
                  "else": [{ "op": "return", "value": { "i32": 0 } }]
                }
              ]
            }
          ]
        }
        """;

        var interpreted = await ExecuteAsync(
            moduleJson,
            SandboxPolicyBuilder.Create().Build(),
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });
        var compiled = await ExecuteAsync(
            moduleJson,
            SandboxPolicyBuilder.Create().Build(),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        Assert.Equal(((I32Value)interpreted.Value!).Value, ((I32Value)compiled.Value!).Value);
        Assert.Equal(41, ((I32Value)compiled.Value).Value);
        Assert.Equal(ExecutionMode.Compiled, compiled.ActualMode);
        Assert.True(compiled.ResourceUsage.AllocatedBytes > 0);
        Assert.True(compiled.ResourceUsage.CollectionElements > 0);
    }

    [Fact]
    public async Task Compiled_map_get_missing_key_returns_safe_error()
    {
        var result = await ExecuteReturnAsync(
            """
            {
              "call": "map.get",
              "args": [
                {
                  "call": "map.empty",
                  "genericType": { "name": "Map", "arguments": ["String", "I32"] },
                  "args": []
                },
                { "string": "missing" }
              ]
            }
            """,
            "\"I32\"",
            SandboxPolicyBuilder.Create().Build());

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.NotFound, result.Error!.Code);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
    }

    [Fact]
    public async Task Compiled_map_entry_limit_is_enforced()
    {
        var result = await ExecuteReturnAsync(
            """
            {
              "call": "map.set",
              "args": [
                {
                  "call": "map.empty",
                  "genericType": { "name": "Map", "arguments": ["String", "I32"] },
                  "args": []
                },
                { "string": "alice" },
                { "i32": 41 }
              ]
            }
            """,
            """{ "name": "Map", "arguments": ["String", "I32"] }""",
            SandboxPolicyBuilder.Create().WithMaxMapEntries(0).Build());

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
    }

    [Fact]
    public async Task Compiled_map_empty_emits_nested_value_type()
    {
        var result = await ExecuteReturnAsync(
            """
            {
              "call": "map.empty",
              "genericType": {
                "name": "Map",
                "arguments": ["String", { "name": "List", "arguments": ["I32"] }]
              },
              "args": []
            }
            """,
            """{ "name": "Map", "arguments": ["String", { "name": "List", "arguments": ["I32"] }] }""",
            SandboxPolicyBuilder.Create().Build());

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        var value = Assert.IsType<MapValue>(result.Value);
        Assert.Equal(SandboxType.String, value.KeyType);
        Assert.Equal(SandboxType.List(SandboxType.I32), value.ValueType);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
    }

    private static async Task<SandboxExecutionResult> ExecuteReturnAsync(
        string expression,
        string returnType,
        SandboxPolicy policy)
    {
        var moduleJson = $$"""
        {
          "id": "compiled-map-return",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": {{returnType}},
              "body": [{ "op": "return", "value": {{expression}} }]
            }
          ]
        }
        """;

        return await ExecuteAsync(
            moduleJson,
            policy,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });
    }

    private static async Task<SandboxExecutionResult> ExecuteAsync(
        string moduleJson,
        SandboxPolicy policy,
        SandboxExecutionOptions options)
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(moduleJson);
        var plan = await host.PrepareAsync(module, policy);
        return await host.ExecuteAsync(plan, "main", SandboxValue.Unit, options);
    }
}
