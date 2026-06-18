using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Compiled.Core;

public sealed class CompiledListCollectionTests
{
    [Fact]
    public async Task Compiled_list_operations_match_interpreter()
    {
        const string expression = """
        {
          "op": "add",
          "left": {
            "op": "add",
            "left": { "call": "list.get", "args": [{ "var": "items" }, { "i32": 0 }] },
            "right": { "call": "list.get", "args": [{ "var": "items" }, { "i32": 1 }] }
          },
          "right": { "call": "list.count", "args": [{ "var": "items" }] }
        }
        """;

        var interpreted = await ExecuteListAsync(
            expression,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });
        var compiled = await ExecuteListAsync(
            expression,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        Assert.Equal(((I32Value)interpreted.Value!).Value, ((I32Value)compiled.Value!).Value);
        Assert.Equal(42, ((I32Value)compiled.Value).Value);
        Assert.Equal(ExecutionMode.Compiled, compiled.ActualMode);
        Assert.True(compiled.ResourceUsage.AllocatedBytes > 0);
        Assert.Equal(3, compiled.ResourceUsage.CollectionElements);
    }

    [Fact]
    public async Task Compiled_list_get_out_of_range_returns_sandbox_error()
    {
        var result = await ExecuteReturnAsync(
            """
            {
              "call": "list.get",
              "args": [
                { "call": "list.of", "args": [{ "i32": 1 }] },
                { "i32": 1 }
              ]
            }
            """,
            "\"I32\"",
            SandboxPolicyBuilder.Create().Build(),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
    }

    [Fact]
    public async Task Compiled_list_length_limit_is_enforced()
    {
        var result = await ExecuteReturnAsync(
            """
            {
              "call": "list.of",
              "args": [{ "i32": 1 }, { "i32": 2 }, { "i32": 3 }]
            }
            """,
            """{ "name": "List", "arguments": ["I32"] }""",
            SandboxPolicyBuilder.Create().WithMaxListLength(2).Build(),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
    }

    [Fact]
    public async Task Compiled_list_empty_emits_generic_item_type()
    {
        var result = await ExecuteReturnAsync(
            """
            { "call": "list.empty", "genericType": "I32", "args": [] }
            """,
            """{ "name": "List", "arguments": ["I32"] }""",
            SandboxPolicyBuilder.Create().Build(),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        var list = Assert.IsType<ListValue>(result.Value);
        Assert.Equal(SandboxType.I32, list.ItemType);
        Assert.Empty(list.Values);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
    }

    [Fact]
    public async Task Compiled_list_add_accepts_record_items()
    {
        var result = await ExecuteReturnAsync(
            """
            {
              "call": "list.add",
              "args": [
                {
                  "call": "list.empty",
                  "genericType": { "name": "Record", "arguments": ["I32", "String"] },
                  "args": []
                },
                {
                  "call": "record.new",
                  "genericType": { "name": "Record", "arguments": ["I32", "String"] },
                  "args": [{ "i32": 7 }, { "string": "ok" }]
                }
              ]
            }
            """,
            """{ "name": "List", "arguments": [{ "name": "Record", "arguments": ["I32", "String"] }] }""",
            SandboxPolicyBuilder.Create().Build(),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        var list = Assert.IsType<ListValue>(result.Value);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        var record = Assert.IsType<RecordValue>(Assert.Single(list.Values));
        Assert.Equal(7, Assert.IsType<I32Value>(record.Fields[0]).Value);
        Assert.Equal("ok", Assert.IsType<StringValue>(record.Fields[1]).Value);
    }

    private static async Task<SandboxExecutionResult> ExecuteListAsync(
        string expression,
        SandboxExecutionOptions options)
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync($$"""
        {
          "id": "compiled-list",
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
                  "name": "items",
                  "value": {
                    "call": "list.add",
                    "args": [
                      { "call": "list.of", "args": [{ "i32": 39 }] },
                      { "i32": 1 }
                    ]
                  }
                },
                { "op": "return", "value": {{expression}} }
              ]
            }
          ]
        }
        """);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build());
        return await host.ExecuteAsync(plan, "main", SandboxValue.Unit, options);
    }

    private static async Task<SandboxExecutionResult> ExecuteReturnAsync(
        string expression,
        string returnType,
        SandboxPolicy policy,
        SandboxExecutionOptions options)
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync($$"""
        {
          "id": "compiled-list-return",
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
        """);
        var plan = await host.PrepareAsync(module, policy);
        return await host.ExecuteAsync(plan, "main", SandboxValue.Unit, options);
    }
}
