using SafeIR;

namespace SafeIR.Tests;

public sealed class SafeCollectionTests
{
    [Fact]
    public async Task List_count_get_and_add_execute_with_allocation_accounting()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync("""
        {
          "id": "collections",
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
                      { "call": "list.empty", "genericType": "I32", "args": [] },
                      { "i32": 40 }
                    ]
                  }
                },
                {
                  "op": "return",
                  "value": {
                    "op": "add",
                    "left": { "call": "list.get", "args": [{ "var": "items" }, { "i32": 0 }] },
                    "right": { "call": "list.count", "args": [{ "var": "items" }] }
                  }
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
    public async Task List_get_reports_out_of_range_as_safe_error()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync("""
        {
          "id": "collections",
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
                    "call": "list.get",
                    "args": [
                      { "call": "list.of", "args": [{ "i32": 1 }] },
                      { "i32": 1 }
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
        Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
    }

    [Fact]
    public async Task List_add_rejects_wrong_item_type_during_validation()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync("""
        {
          "id": "collections",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": { "name": "List", "arguments": ["I32"] },
              "body": [
                {
                  "op": "return",
                  "value": {
                    "call": "list.add",
                    "args": [
                      { "call": "list.empty", "genericType": "I32", "args": [] },
                      { "string": "wrong" }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """);

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-TYPE-MISMATCH");
    }

    [Fact]
    public async Task List_growth_charges_allocation_quota()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync("""
        {
          "id": "collections",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": { "name": "List", "arguments": ["I32"] },
              "body": [
                {
                  "op": "return",
                  "value": {
                    "call": "list.add",
                    "args": [
                      { "call": "list.empty", "genericType": "I32", "args": [] },
                      { "i32": 1 }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithMaxAllocatedBytes(10).Build());

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
    }
}
