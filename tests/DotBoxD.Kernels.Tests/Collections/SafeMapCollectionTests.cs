using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
namespace DotBoxD.Kernels.Tests.Collections;
public sealed class SafeMapCollectionTests
{
    [Fact]
    public async Task Map_set_get_and_contains_execute_with_allocation_accounting()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync("""
        {
          "id": "maps",
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
                  "op": "if",
                  "condition": {
                    "call": "map.containsKey",
                    "args": [{ "var": "scores" }, { "string": "alice" }]
                  },
                  "then": [
                    {
                      "op": "return",
                      "value": {
                        "call": "map.get",
                        "args": [{ "var": "scores" }, { "string": "alice" }]
                      }
                    }
                  ],
                  "else": [{ "op": "return", "value": { "i32": 0 } }]
                }
              ]
            }
          ]
        }
        """);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithMaxAllocatedBytes(128).Build());
        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);
        Assert.True(result.Succeeded);
        Assert.Equal(41, ((I32Value)result.Value!).Value);
        Assert.True(result.ResourceUsage.AllocatedBytes > 0);
    }

    [Fact]
    public async Task Map_remove_returns_map_without_key()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync("""
        {
          "id": "maps",
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
                    "args": [{ "var": "scores" }, { "string": "alice" }]
                  }
                },
                {
                  "op": "if",
                  "condition": {
                    "call": "map.containsKey",
                    "args": [{ "var": "trimmed" }, { "string": "alice" }]
                  },
                  "then": [{ "op": "return", "value": { "i32": 1 } }],
                  "else": [{ "op": "return", "value": { "i32": 0 } }]
                }
              ]
            }
          ]
        }
        """);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build());
        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded);
        Assert.Equal(0, ((I32Value)result.Value!).Value);
    }

    [Fact]
    public async Task Map_get_reports_missing_key_as_safe_error()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync("""
        {
          "id": "maps",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                {
                  "op": "return",
                  "value": {
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
                }
              ]
            }
          ]
        }
        """);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build());
        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.NotFound, result.Error!.Code);
    }

    [Theory]
    [InlineData("key", """
        {
          "call": "map.containsKey",
          "args": [
            {
              "call": "map.empty",
              "genericType": { "name": "Map", "arguments": ["String", "I32"] },
              "args": []
            },
            { "i32": 1 }
          ]
        }
        """)]
    [InlineData("value", """
        {
          "call": "map.set",
          "args": [
            {
              "call": "map.empty",
              "genericType": { "name": "Map", "arguments": ["String", "I32"] },
              "args": []
            },
            { "string": "alice" },
            { "string": "wrong" }
          ]
        }
        """)]
    public async Task Map_operations_reject_type_mismatches_during_validation(string _, string expression)
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync($$"""
        {
          "id": "maps",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Bool",
              "body": [{ "op": "return", "value": {{expression}} }]
            }
          ]
        }
        """);
        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-TYPE-MISMATCH");
    }

    [Fact]
    public async Task Map_empty_rejects_non_hashable_key_type()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync("""
        {
          "id": "maps",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": { "name": "Map", "arguments": [{ "name": "List", "arguments": ["I32"] }, "I32"] },
              "body": [
                {
                  "op": "return",
                  "value": {
                    "call": "map.empty",
                    "genericType": {
                      "name": "Map",
                      "arguments": [{ "name": "List", "arguments": ["I32"] }, "I32"]
                    },
                    "args": []
                  }
                }
              ]
            }
          ]
        }
        """);
        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-TYPE-MAP-KEY");
    }

    [Fact]
    public async Task Map_growth_charges_allocation_quota()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync("""
        {
          "id": "maps",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": { "name": "Map", "arguments": ["String", "I32"] },
              "body": [
                {
                  "op": "return",
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
                }
              ]
            }
          ]
        }
        """);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithMaxAllocatedBytes(20).Build());
        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
    }
}
