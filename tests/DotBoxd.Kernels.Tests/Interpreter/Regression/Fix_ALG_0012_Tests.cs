namespace DotBoxd.Kernels.Tests;

// Regression coverage for ALG-0012: the function analyzer now uses a parent-linked,
// copy-on-write scope instead of cloning the full local dictionary per control-flow
// block. These tests pin the observable lexical-scope behavior that the rewrite must
// preserve: child-block writes stay local, parent locals remain visible inside nested
// blocks, and type-conflict detection still walks the parent chain.
public sealed class Fix_ALG_0012_Tests
{
    [Fact]
    public async Task Local_introduced_in_then_branch_is_not_visible_after_branch()
    {
        var ex = await PrepareFailsAsync("""
        {
          "id": "alg-0012-branch-leak",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "flag", "type": "Bool" }],
              "returnType": "I32",
              "body": [
                {
                  "op": "if",
                  "condition": { "var": "flag" },
                  "then": [{ "op": "set", "name": "branchLocal", "value": { "i32": 1 } }],
                  "else": []
                },
                { "op": "return", "value": { "var": "branchLocal" } }
              ]
            }
          ]
        }
        """);

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-LOCAL-UNKNOWN");
    }

    [Fact]
    public async Task Local_introduced_in_then_branch_is_not_visible_in_else_branch()
    {
        var ex = await PrepareFailsAsync("""
        {
          "id": "alg-0012-sibling-leak",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "flag", "type": "Bool" }],
              "returnType": "I32",
              "body": [
                {
                  "op": "if",
                  "condition": { "var": "flag" },
                  "then": [
                    { "op": "set", "name": "thenLocal", "value": { "i32": 1 } },
                    { "op": "return", "value": { "var": "thenLocal" } }
                  ],
                  "else": [{ "op": "return", "value": { "var": "thenLocal" } }]
                }
              ]
            }
          ]
        }
        """);

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-LOCAL-UNKNOWN");
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted, false)]
    [InlineData(ExecutionMode.Compiled, true)]
    public async Task Sibling_branch_locals_can_reuse_name_with_different_types(
        ExecutionMode mode,
        bool compiler)
    {
        var host = SandboxTestHost.Create(compiler: compiler);
        var module = await host.ImportJsonAsync("""
        {
          "id": "alg-0012-sibling-type-reuse",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "flag", "type": "Bool" }],
              "returnType": "I32",
              "body": [
                {
                  "op": "if",
                  "condition": { "var": "flag" },
                  "then": [
                    { "op": "set", "name": "value", "value": { "i32": 7 } },
                    { "op": "return", "value": { "var": "value" } }
                  ],
                  "else": [
                    { "op": "set", "name": "value", "value": { "bool": true } },
                    { "op": "return", "value": { "i32": 3 } }
                  ]
                }
              ]
            }
          ]
        }
        """);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var options = new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false };

        var trueResult = await host.ExecuteAsync(plan, "main", SandboxValue.FromBool(true), options);
        var falseResult = await host.ExecuteAsync(plan, "main", SandboxValue.FromBool(false), options);

        Assert.True(trueResult.Succeeded, trueResult.Error?.SafeMessage);
        Assert.True(falseResult.Succeeded, falseResult.Error?.SafeMessage);
        Assert.Equal(mode, trueResult.ActualMode);
        Assert.Equal(mode, falseResult.ActualMode);
        Assert.Equal(7, ((I32Value)trueResult.Value!).Value);
        Assert.Equal(3, ((I32Value)falseResult.Value!).Value);
    }

    [Fact]
    public async Task Parent_local_is_visible_and_reassignable_inside_nested_blocks()
    {
        // outer local "acc" is read and rewritten inside a forRange body and a while body.
        // The copy-on-write scope must resolve the parent local through the chain, so this
        // program validates and executes without E-LOCAL-UNKNOWN.
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync("""
        {
          "id": "alg-0012-nested-visible",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "n", "type": "I32" }],
              "returnType": "I32",
              "body": [
                { "op": "set", "name": "acc", "value": { "i32": 0 } },
                {
                  "op": "forRange",
                  "local": "i",
                  "start": { "i32": 0 },
                  "end": { "var": "n" },
                  "body": [
                    {
                      "op": "set",
                      "name": "acc",
                      "value": { "op": "add", "left": { "var": "acc" }, "right": { "var": "i" } }
                    }
                  ]
                },
                {
                  "op": "while",
                  "condition": { "op": "lt", "left": { "var": "acc" }, "right": { "i32": 0 } },
                  "body": [
                    { "op": "set", "name": "acc", "value": { "i32": 0 } }
                  ]
                },
                { "op": "return", "value": { "var": "acc" } }
              ]
            }
          ]
        }
        """);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.FromInt32(4));

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(6, ((I32Value)result.Value!).Value);
    }

    [Fact]
    public async Task Reassigning_parent_local_to_different_type_in_nested_block_is_rejected()
    {
        // "value" is an I32 parent local; the nested branch tries to rebind it to Bool.
        // Type-conflict detection must walk the parent chain and still emit E-LOCAL-TYPE.
        var ex = await PrepareFailsAsync("""
        {
          "id": "alg-0012-nested-type-conflict",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "flag", "type": "Bool" }],
              "returnType": "I32",
              "body": [
                { "op": "set", "name": "value", "value": { "i32": 0 } },
                {
                  "op": "if",
                  "condition": { "var": "flag" },
                  "then": [{ "op": "set", "name": "value", "value": { "bool": true } }],
                  "else": []
                },
                { "op": "return", "value": { "var": "value" } }
              ]
            }
          ]
        }
        """);

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-LOCAL-TYPE");
    }

    private static async Task<SandboxValidationException> PrepareFailsAsync(string json)
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(json);
        return await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build()));
    }
}
