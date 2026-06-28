using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.BranchMerge;

public sealed class BranchMergeScopeTests
{
    [Fact]
    public async Task Local_assigned_in_both_branches_is_visible_after_branch()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync("""
        {
          "id": "branch-merge-both",
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
                  "then": [{ "op": "set", "name": "value", "value": { "i32": 1 } }],
                  "else": [{ "op": "set", "name": "value", "value": { "i32": 2 } }]
                },
                { "op": "return", "value": { "var": "value" } }
              ]
            }
          ]
        }
        """);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var whenTrue = await host.ExecuteAsync(plan, "main", SandboxValue.FromBool(true));
        var whenFalse = await host.ExecuteAsync(plan, "main", SandboxValue.FromBool(false));

        Assert.True(whenTrue.Succeeded, whenTrue.Error?.SafeMessage);
        Assert.True(whenFalse.Succeeded, whenFalse.Error?.SafeMessage);
        Assert.Equal(1, Assert.IsType<I32Value>(whenTrue.Value).Value);
        Assert.Equal(2, Assert.IsType<I32Value>(whenFalse.Value).Value);
    }

    [Fact]
    public async Task Local_assigned_on_only_continuing_branch_is_visible_after_branch()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync("""
        {
          "id": "branch-merge-returning-side",
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
                  "then": [{ "op": "return", "value": { "i32": 1 } }],
                  "else": [{ "op": "set", "name": "value", "value": { "i32": 2 } }]
                },
                { "op": "return", "value": { "var": "value" } }
              ]
            }
          ]
        }
        """);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var whenTrue = await host.ExecuteAsync(plan, "main", SandboxValue.FromBool(true));
        var whenFalse = await host.ExecuteAsync(plan, "main", SandboxValue.FromBool(false));

        Assert.True(whenTrue.Succeeded, whenTrue.Error?.SafeMessage);
        Assert.True(whenFalse.Succeeded, whenFalse.Error?.SafeMessage);
        Assert.Equal(1, Assert.IsType<I32Value>(whenTrue.Value).Value);
        Assert.Equal(2, Assert.IsType<I32Value>(whenFalse.Value).Value);
    }
}
