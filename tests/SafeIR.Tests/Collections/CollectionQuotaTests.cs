using SafeIR;

namespace SafeIR.Tests;

public sealed class CollectionQuotaTests
{
    [Fact]
    public async Task List_length_limit_is_enforced()
    {
        var result = await ExecuteReturnAsync(
            """
            { "call": "list.of", "args": [{ "i32": 1 }, { "i32": 2 }, { "i32": 3 }] }
            """,
            """{ "name": "List", "arguments": ["I32"] }""",
            SandboxPolicyBuilder.Create().WithMaxListLength(2).Build());

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
    }

    [Fact]
    public async Task Map_entry_limit_is_enforced()
    {
        var result = await ExecuteReturnAsync(
            """
            {
              "call": "map.set",
              "args": [
                {
                  "call": "map.set",
                  "args": [
                    {
                      "call": "map.empty",
                      "genericType": { "name": "Map", "arguments": ["String", "I32"] },
                      "args": []
                    },
                    { "string": "a" },
                    { "i32": 1 }
                  ]
                },
                { "string": "b" },
                { "i32": 2 }
              ]
            }
            """,
            """{ "name": "Map", "arguments": ["String", "I32"] }""",
            SandboxPolicyBuilder.Create().WithMaxMapEntries(1).Build());

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
    }

    [Fact]
    public async Task Collection_depth_limit_is_enforced()
    {
        var result = await ExecuteReturnAsync(
            """
            {
              "call": "list.of",
              "args": [{ "call": "list.empty", "genericType": "I32", "args": [] }]
            }
            """,
            """{ "name": "List", "arguments": [{ "name": "List", "arguments": ["I32"] }] }""",
            SandboxPolicyBuilder.Create().WithMaxCollectionDepth(1).Build());

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
    }

    [Fact]
    public async Task Total_collection_element_limit_counts_entrypoint_input()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithMaxTotalCollectionElements(1).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(2)]));

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
    }

    [Fact]
    public void Collection_limits_are_part_of_policy_hash()
    {
        var first = SandboxPolicyBuilder.Create().WithMaxListLength(1).Build();
        var second = SandboxPolicyBuilder.Create().WithMaxListLength(2).Build();

        Assert.NotEqual(first.Hash, second.Hash);
    }

    [Fact]
    public async Task Host_collection_input_is_snapshotted_before_later_mutation()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync("""
        {
          "id": "cyclic-input",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "input", "type": { "name": "Map", "arguments": ["String", "I32"] } }],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "i32": 1 } }]
            }
          ]
        }
        """);
        var values = new Dictionary<SandboxValue, SandboxValue>();
        var input = SandboxValue.FromMap(values, SandboxType.String, SandboxType.I32);
        values[SandboxValue.FromString("self")] = input;
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build());

        var result = await host.ExecuteAsync(plan, "main", input);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(1, ((I32Value)result.Value!).Value);
    }

    [Fact]
    public async Task Deep_host_collection_input_fails_by_quota_without_recursion()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync("""
        {
          "id": "deep-input",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "i32": 1 } }]
            }
          ]
        }
        """);
        SandboxValue input = SandboxValue.FromInt32(0);
        for (var i = 0; i < 2_048; i++) {
            input = new ListValue([input], input.Type);
        }

        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithMaxCollectionDepth(8).Build());

        var result = await host.ExecuteAsync(plan, "main", input);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
    }

    [Fact]
    public async Task Single_list_entrypoint_input_is_validated_deeply()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync("""
        {
          "id": "typed-list-input",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "input", "type": { "name": "List", "arguments": ["I32"] } }],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "call": "list.count", "args": [{ "var": "input" }] } }]
            }
          ]
        }
        """);
        var input = new ListValue([SandboxValue.FromString("wrong")], SandboxType.I32);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build());

        var result = await host.ExecuteAsync(plan, "main", input);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
    }

    private static async Task<SandboxExecutionResult> ExecuteReturnAsync(
        string expression,
        string returnType,
        SandboxPolicy policy)
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync($$"""
        {
          "id": "collection-quotas",
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
        return await host.ExecuteAsync(plan, "main", SandboxValue.Unit);
    }
}
