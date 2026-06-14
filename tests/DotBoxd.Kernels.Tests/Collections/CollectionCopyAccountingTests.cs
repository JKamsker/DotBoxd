using DotBoxd.Kernels;

namespace DotBoxd.Kernels.Tests;

public sealed class CollectionCopyAccountingTests
{
    [Fact]
    public void Collection_copy_allocation_widens_large_counts_before_multiplication()
    {
        Assert.Equal(34_359_738_368, SandboxCollectionFuel.AllocationBytes(
            int.MaxValue,
            addedCount: 1,
            bytesPerElement: 16));
        Assert.Equal(68_719_476_736, SandboxCollectionFuel.AllocationBytes(
            int.MaxValue,
            addedCount: 1,
            bytesPerElement: 32,
            minimumOne: true));
        Assert.Equal(68_719_476_704, SandboxCollectionFuel.AllocationBytes(int.MaxValue, 32, minimumOne: true));
    }

    [Fact]
    public async Task List_add_charges_projected_copy_allocation_before_copying_source()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync("""
        {
          "id": "list-copy-accounting",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [
                { "name": "input", "type": { "name": "List", "arguments": ["I32"] } }
              ],
              "returnType": { "name": "List", "arguments": ["I32"] },
              "body": [
                {
                  "op": "return",
                  "value": {
                    "call": "list.add",
                    "args": [{ "var": "input" }, { "i32": 2 }]
                  }
                }
              ]
            }
          ]
        }
        """);
        var input = SandboxValue.FromList([SandboxValue.FromInt32(1)], SandboxType.I32);
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create().WithMaxAllocatedBytes(16).Build());

        var result = await host.ExecuteAsync(plan, "main", input);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        Assert.Equal(32, result.ResourceUsage.AllocatedBytes);
    }

    [Fact]
    public async Task Compiled_list_add_charges_projected_copy_allocation_before_copying_source()
    {
        var result = await ExecuteCompiledCopyAsync(
            """
            {
              "id": "compiled-list-copy-accounting",
              "version": "1.0.0",
              "functions": [
                {
                  "id": "main",
                  "visibility": "entrypoint",
                  "parameters": [
                    { "name": "input", "type": { "name": "List", "arguments": ["I32"] } }
                  ],
                  "returnType": { "name": "List", "arguments": ["I32"] },
                  "body": [
                    {
                      "op": "return",
                      "value": {
                        "call": "list.add",
                        "args": [{ "var": "input" }, { "i32": 2 }]
                      }
                    }
                  ]
                }
              ]
            }
            """,
            SandboxValue.FromList([SandboxValue.FromInt32(1)], SandboxType.I32),
            SandboxPolicyBuilder.Create().WithMaxAllocatedBytes(16).Build());

        AssertCopyQuota(result, 32);
    }

    [Fact]
    public async Task Compiled_map_set_charges_projected_copy_allocation_before_copying_source()
    {
        var result = await ExecuteCompiledCopyAsync(
            MapMutationModule(
                """
                {
                  "call": "map.set",
                  "args": [{ "var": "input" }, { "i32": 2 }, { "i32": 20 }]
                }
                """),
            I32MapWithOneEntry(),
            SandboxPolicyBuilder.Create().WithMaxAllocatedBytes(32).Build());

        AssertCopyQuota(result, 64);
    }

    [Fact]
    public async Task Compiled_map_remove_charges_projected_copy_allocation_before_copying_source()
    {
        var result = await ExecuteCompiledCopyAsync(
            MapMutationModule(
                """
                {
                  "call": "map.remove",
                  "args": [{ "var": "input" }, { "i32": 1 }]
                }
                """),
            I32MapWithOneEntry(),
            SandboxPolicyBuilder.Create().WithMaxAllocatedBytes(16).Build());

        AssertCopyQuota(result, 32);
    }

    private static async Task<SandboxExecutionResult> ExecuteCompiledCopyAsync(
        string moduleJson,
        SandboxValue input,
        SandboxPolicy policy)
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(moduleJson);
        var plan = await host.PrepareAsync(module, policy);
        return await host.ExecuteAsync(
            plan,
            "main",
            input,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });
    }

    private static string MapMutationModule(string expression)
        => $$"""
        {
          "id": "compiled-map-copy-accounting",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [
                {
                  "name": "input",
                  "type": { "name": "Map", "arguments": ["I32", "I32"] }
                }
              ],
              "returnType": { "name": "Map", "arguments": ["I32", "I32"] },
              "body": [{ "op": "return", "value": {{expression}} }]
            }
          ]
        }
        """;

    private static SandboxValue I32MapWithOneEntry()
        => SandboxValue.FromMap(
            new Dictionary<SandboxValue, SandboxValue>
            {
                [SandboxValue.FromInt32(1)] = SandboxValue.FromInt32(10)
            },
            SandboxType.I32,
            SandboxType.I32);

    private static void AssertCopyQuota(SandboxExecutionResult result, long allocatedBytes)
    {
        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.Equal(allocatedBytes, result.ResourceUsage.AllocatedBytes);
    }
}
