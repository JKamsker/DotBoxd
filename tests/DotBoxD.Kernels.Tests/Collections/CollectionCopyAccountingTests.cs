using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Collections;

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

    [Fact]
    public async Task Map_remove_existing_key_is_equivalent_across_modes()
    {
        var interpreted = await ExecuteMapRemoveAsync(removeKey: 1, lookupKey: 2, ExecutionMode.Interpreted);
        var compiled = await ExecuteMapRemoveAsync(removeKey: 1, lookupKey: 2, ExecutionMode.Compiled);

        AssertMapRemoveEquivalent(interpreted, compiled, expectedLookup: 20);
    }

    [Fact]
    public async Task Map_remove_missing_key_is_equivalent_across_modes()
    {
        var interpreted = await ExecuteMapRemoveAsync(removeKey: 9, lookupKey: 1, ExecutionMode.Interpreted);
        var compiled = await ExecuteMapRemoveAsync(removeKey: 9, lookupKey: 1, ExecutionMode.Compiled);

        AssertMapRemoveEquivalent(interpreted, compiled, expectedLookup: 10);
    }

    private static async Task<SandboxExecutionResult> ExecuteMapRemoveAsync(
        int removeKey,
        int lookupKey,
        ExecutionMode mode)
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync($$"""
        {
          "id": "map-remove-differential",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [
                { "name": "input", "type": { "name": "Map", "arguments": ["I32", "I32"] } }
              ],
              "returnType": "I32",
              "body": [
                {
                  "op": "set",
                  "name": "trimmed",
                  "value": {
                    "call": "map.remove",
                    "args": [{ "var": "input" }, { "i32": {{removeKey}} }]
                  }
                },
                {
                  "op": "return",
                  "value": {
                    "call": "map.get",
                    "args": [{ "var": "trimmed" }, { "i32": {{lookupKey}} }]
                  }
                }
              ]
            }
          ]
        }
        """);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build());
        return await host.ExecuteAsync(
            plan,
            "main",
            I32MapWithTwoEntries(),
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });
    }

    private static SandboxValue I32MapWithTwoEntries()
        => SandboxValue.FromMap(
            new Dictionary<SandboxValue, SandboxValue>
            {
                [SandboxValue.FromInt32(1)] = SandboxValue.FromInt32(10),
                [SandboxValue.FromInt32(2)] = SandboxValue.FromInt32(20)
            },
            SandboxType.I32,
            SandboxType.I32);

    // map.remove now shares immutable backing instead of deep-revalidating + copying the source map. The result
    // contents and the shape-derived charges (AllocatedBytes/CollectionElements) must stay identical to the
    // interpreter for both an existing-key removal and a no-op missing-key removal.
    private static void AssertMapRemoveEquivalent(
        SandboxExecutionResult interpreted,
        SandboxExecutionResult compiled,
        int expectedLookup)
    {
        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, interpreted.ActualMode);
        Assert.Equal(ExecutionMode.Compiled, compiled.ActualMode);
        Assert.Equal(expectedLookup, ((I32Value)interpreted.Value!).Value);
        Assert.Equal(expectedLookup, ((I32Value)compiled.Value!).Value);
        Assert.Equal(interpreted.ResourceUsage.AllocatedBytes, compiled.ResourceUsage.AllocatedBytes);
        Assert.Equal(interpreted.ResourceUsage.CollectionElements, compiled.ResourceUsage.CollectionElements);
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
