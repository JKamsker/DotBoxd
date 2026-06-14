namespace SafeIR.Tests;

using SafeIR;
using SafeIR.Runtime;

/// <summary>
/// Regression coverage for ALG-0008: read-only collection helpers
/// (<c>list.count</c>, <c>list.get</c>, <c>map.containsKey</c>, <c>map.get</c>) used to route
/// through <c>AsList</c>/<c>AsMap</c>, which recursively re-validated every element of the
/// whole collection via <see cref="SandboxValueValidator.RequireType(SandboxValue, SandboxType, string)"/> before performing the
/// O(1) lookup. That made repeated reads scale with collection size in both the interpreter
/// and the compiled runtime, even though contents are already validated at trust boundaries
/// (entrypoint inputs and binding returns) and stay typed through every internal constructor.
///
/// The fix gives reads a shallow kind-only helper. These tests pin the observable behaviour
/// that must be preserved: reads still return correct results on multi-element collections,
/// and the shallow helper still fails closed when handed a value of the wrong runtime kind.
/// </summary>
public sealed class Fix_ALG_0008_Tests
{
    private static SandboxContext CreateContext()
    {
        var policy = SandboxPolicyBuilder.Create().WithFuel(1_000_000).Build();
        return new SandboxContext(
            SandboxRunId.New(),
            policy,
            new ResourceMeter(policy.ResourceLimits),
            new BindingRegistryBuilder().Build(),
            new InMemoryAuditSink(),
            CancellationToken.None);
    }

    [Fact]
    public void List_count_and_get_return_correct_results_for_multi_element_list()
    {
        var context = CreateContext();
        var list = SandboxValue.FromList(
            Enumerable.Range(0, 256).Select(SandboxValue.FromInt32).ToArray(),
            SandboxType.I32);

        var count = CompiledRuntime.ListCount(context, list);
        var item = CompiledRuntime.ListGet(context, list, SandboxValue.FromInt32(200));

        Assert.Equal(256, ((I32Value)count).Value);
        Assert.Equal(200, ((I32Value)item).Value);
    }

    [Fact]
    public void Map_containsKey_and_get_return_correct_results_for_multi_entry_map()
    {
        var context = CreateContext();
        var entries = new Dictionary<SandboxValue, SandboxValue>();
        for (var i = 0; i < 256; i++)
        {
            entries[SandboxValue.FromInt32(i)] = SandboxValue.FromInt32(i * 2);
        }

        var map = SandboxValue.FromMap(entries, SandboxType.I32, SandboxType.I32);

        var contains = CompiledRuntime.MapContainsKey(context, map, SandboxValue.FromInt32(128));
        var missing = CompiledRuntime.MapContainsKey(context, map, SandboxValue.FromInt32(999));
        var value = CompiledRuntime.MapGet(context, map, SandboxValue.FromInt32(128));

        Assert.True(((BoolValue)contains).Value);
        Assert.False(((BoolValue)missing).Value);
        Assert.Equal(256, ((I32Value)value).Value);
    }

    [Fact]
    public void List_read_helpers_fail_closed_when_value_is_not_a_list()
    {
        var context = CreateContext();
        var notAList = SandboxValue.FromInt32(7);

        var count = Assert.Throws<SandboxRuntimeException>(() => CompiledRuntime.ListCount(context, notAList));
        var get = Assert.Throws<SandboxRuntimeException>(
            () => CompiledRuntime.ListGet(context, notAList, SandboxValue.FromInt32(0)));

        Assert.Equal(SandboxErrorCode.InvalidInput, count.Error.Code);
        Assert.Equal(SandboxErrorCode.InvalidInput, get.Error.Code);
    }

    [Fact]
    public void Map_read_helpers_fail_closed_when_value_is_not_a_map()
    {
        var context = CreateContext();
        var notAMap = SandboxValue.FromInt32(7);

        var contains = Assert.Throws<SandboxRuntimeException>(
            () => CompiledRuntime.MapContainsKey(context, notAMap, SandboxValue.FromInt32(0)));
        var get = Assert.Throws<SandboxRuntimeException>(
            () => CompiledRuntime.MapGet(context, notAMap, SandboxValue.FromInt32(0)));

        Assert.Equal(SandboxErrorCode.InvalidInput, contains.Error.Code);
        Assert.Equal(SandboxErrorCode.InvalidInput, get.Error.Code);
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public async Task Repeated_list_reads_stay_correct_end_to_end(ExecutionMode mode)
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync("""
        {
          "id": "alg-0008-list-reads",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [
                { "name": "items", "type": { "name": "List", "arguments": ["I32"] } }
              ],
              "returnType": "I32",
              "body": [
                {
                  "op": "return",
                  "value": {
                    "op": "add",
                    "left": { "call": "list.count", "args": [{ "var": "items" }] },
                    "right": { "call": "list.get", "args": [{ "var": "items" }, { "i32": 3 }] }
                  }
                }
              ]
            }
          ]
        }
        """);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(100_000).Build());
        var input = SandboxValue.FromList(
            Enumerable.Range(0, 64).Select(SandboxValue.FromInt32).ToArray(),
            SandboxType.I32);

        var result = await host.ExecuteAsync(
            plan,
            "main",
            input,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(67, ((I32Value)result.Value!).Value);
        Assert.Equal(mode, result.ActualMode);
    }
}
