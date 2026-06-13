using SafeIR;

namespace SafeIR.Tests;

/// <summary>
/// Regression coverage for COR-0059: collection equality must compare values
/// structurally instead of comparing backing-collection identity. Two
/// independently materialized lists or maps with equal contents must compare
/// equal in both the interpreter and the compiled runtime, which both dispatch
/// through <see cref="object.Equals(object, object)"/> on the sandbox value.
/// </summary>
public sealed class Fix_COR_0059_Tests
{
    [Fact]
    public void Equal_lists_built_from_separate_instances_compare_equal()
    {
        var left = SandboxValue.FromList(new[] { SandboxValue.FromInt32(1), SandboxValue.FromInt32(2) });
        var right = SandboxValue.FromList(new[] { SandboxValue.FromInt32(1), SandboxValue.FromInt32(2) });

        Assert.True(Equals(left, right));
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void Lists_with_different_elements_compare_unequal()
    {
        var left = SandboxValue.FromList(new[] { SandboxValue.FromInt32(1), SandboxValue.FromInt32(2) });
        var right = SandboxValue.FromList(new[] { SandboxValue.FromInt32(1), SandboxValue.FromInt32(3) });

        Assert.False(Equals(left, right));
    }

    [Fact]
    public void Lists_with_different_lengths_compare_unequal()
    {
        var left = SandboxValue.FromList(new[] { SandboxValue.FromInt32(1) });
        var right = SandboxValue.FromList(new[] { SandboxValue.FromInt32(1), SandboxValue.FromInt32(2) });

        Assert.False(Equals(left, right));
    }

    [Fact]
    public void Empty_lists_of_different_item_types_compare_unequal()
    {
        var left = SandboxValue.FromList(Array.Empty<SandboxValue>(), SandboxType.I32);
        var right = SandboxValue.FromList(Array.Empty<SandboxValue>(), SandboxType.String);

        Assert.False(Equals(left, right));
    }

    [Fact]
    public void Equal_maps_built_from_separate_instances_compare_equal()
    {
        var left = BuildMap(("alice", 1), ("bob", 2));
        var right = BuildMap(("bob", 2), ("alice", 1));

        Assert.True(Equals(left, right));
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void Maps_with_different_values_compare_unequal()
    {
        var left = BuildMap(("alice", 1));
        var right = BuildMap(("alice", 2));

        Assert.False(Equals(left, right));
    }

    [Fact]
    public void Maps_with_different_keys_compare_unequal()
    {
        var left = BuildMap(("alice", 1));
        var right = BuildMap(("bob", 1));

        Assert.False(Equals(left, right));
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public async Task Equal_lists_from_separate_expressions_evaluate_equal(ExecutionMode mode)
    {
        var result = await ExecuteEqualityAsync(
            """
            {
              "op": "eq",
              "left": { "call": "list.of", "args": [{ "i32": 1 }, { "i32": 2 }] },
              "right": { "call": "list.of", "args": [{ "i32": 1 }, { "i32": 2 }] }
            }
            """,
            mode);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.True(((BoolValue)result.Value!).Value);
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public async Task Unequal_lists_from_separate_expressions_evaluate_unequal(ExecutionMode mode)
    {
        var result = await ExecuteEqualityAsync(
            """
            {
              "op": "eq",
              "left": { "call": "list.of", "args": [{ "i32": 1 }, { "i32": 2 }] },
              "right": { "call": "list.of", "args": [{ "i32": 1 }, { "i32": 3 }] }
            }
            """,
            mode);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.False(((BoolValue)result.Value!).Value);
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public async Task Equal_maps_from_separate_expressions_evaluate_equal(ExecutionMode mode)
    {
        var result = await ExecuteEqualityAsync(
            """
            {
              "op": "eq",
              "left": {
                "call": "map.set",
                "args": [
                  { "call": "map.empty", "genericType": { "name": "Map", "arguments": ["String", "I32"] }, "args": [] },
                  { "string": "alice" },
                  { "i32": 1 }
                ]
              },
              "right": {
                "call": "map.set",
                "args": [
                  { "call": "map.empty", "genericType": { "name": "Map", "arguments": ["String", "I32"] }, "args": [] },
                  { "string": "alice" },
                  { "i32": 1 }
                ]
              }
            }
            """,
            mode);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.True(((BoolValue)result.Value!).Value);
    }

    private static MapValue BuildMap(params (string Key, int Value)[] entries)
    {
        var values = new Dictionary<SandboxValue, SandboxValue>();
        foreach (var (key, value) in entries)
        {
            values[SandboxValue.FromString(key)] = SandboxValue.FromInt32(value);
        }

        return (MapValue)SandboxValue.FromMap(values, SandboxType.String, SandboxType.I32);
    }

    private static async Task<SandboxExecutionResult> ExecuteEqualityAsync(string expression, ExecutionMode mode)
    {
        var host = SandboxTestHost.Create(compiler: mode == ExecutionMode.Compiled);
        var module = await host.ImportJsonAsync($$"""
        {
          "id": "cor-0059-equality",
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
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build());
        return await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });
    }
}
