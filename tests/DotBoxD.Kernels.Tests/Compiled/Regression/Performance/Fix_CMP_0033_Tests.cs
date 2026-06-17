using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance;

public sealed class Fix_CMP_0033_Tests
{
    [Theory]
    [InlineData(CollectionLoopShape.ListCount)]
    [InlineData(CollectionLoopShape.ListGet)]
    [InlineData(CollectionLoopShape.MapGet)]
    public async Task Collection_loop_fast_paths_do_not_charge_collection_work_before_loop_quota(
        CollectionLoopShape shape)
    {
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(10_000)
            .WithMaxLoopIterations(0)
            .Build();
        var input = InputFor(shape);
        var interpreted = await ExecuteAsync(LoopQuotaModuleJson(shape), policy, ExecutionMode.Interpreted, input);
        var compiled = await ExecuteAsync(LoopQuotaModuleJson(shape), policy, ExecutionMode.Compiled, input);

        AssertQuotaExceeded(interpreted, ExecutionMode.Interpreted);
        AssertQuotaExceeded(compiled, ExecutionMode.Compiled);
        Assert.Equal(interpreted.ResourceUsage.LoopIterations, compiled.ResourceUsage.LoopIterations);
        Assert.True(
            compiled.ResourceUsage.FuelUsed <= interpreted.ResourceUsage.FuelUsed + 8,
            $"compiled fuel {compiled.ResourceUsage.FuelUsed} must not include bulk collection reads before loop quota; interpreted fuel was {interpreted.ResourceUsage.FuelUsed}");
        Assert.Equal(interpreted.ResourceUsage.StringBytes, compiled.ResourceUsage.StringBytes);
    }

    [Fact]
    public async Task Map_get_fast_path_charges_loop_iterations_before_missing_key_failure()
    {
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(10_000)
            .WithMaxLoopIterations(10)
            .Build();
        var input = InputFor(CollectionLoopShape.MapGet);
        var interpreted = await ExecuteAsync(MapGetMissingKeyModuleJson(), policy, ExecutionMode.Interpreted, input);
        var compiled = await ExecuteAsync(MapGetMissingKeyModuleJson(), policy, ExecutionMode.Compiled, input);

        AssertError(interpreted, ExecutionMode.Interpreted, SandboxErrorCode.NotFound);
        AssertError(compiled, ExecutionMode.Compiled, SandboxErrorCode.NotFound);
        Assert.Equal(interpreted.ResourceUsage.LoopIterations, compiled.ResourceUsage.LoopIterations);
    }

    [Fact]
    public async Task List_get_fast_path_charges_loop_iterations_before_out_of_range_failure()
    {
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(10_000)
            .WithMaxLoopIterations(10)
            .Build();
        var input = InputFor(CollectionLoopShape.ListGet);
        var interpreted = await ExecuteAsync(ListGetOutOfRangeModuleJson(), policy, ExecutionMode.Interpreted, input);
        var compiled = await ExecuteAsync(ListGetOutOfRangeModuleJson(), policy, ExecutionMode.Compiled, input);

        AssertError(interpreted, ExecutionMode.Interpreted, SandboxErrorCode.InvalidInput);
        AssertError(compiled, ExecutionMode.Compiled, SandboxErrorCode.InvalidInput);
        Assert.Equal(interpreted.ResourceUsage.LoopIterations, compiled.ResourceUsage.LoopIterations);
    }

    private static async Task<SandboxExecutionResult> ExecuteAsync(
        string moduleJson,
        SandboxPolicy policy,
        ExecutionMode mode,
        SandboxValue input)
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(moduleJson);
        var plan = await host.PrepareAsync(module, policy);
        return await host.ExecuteAsync(
            plan,
            "main",
            input,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });
    }

    private static void AssertQuotaExceeded(SandboxExecutionResult result, ExecutionMode mode)
        => AssertError(result, mode, SandboxErrorCode.QuotaExceeded);

    private static void AssertError(
        SandboxExecutionResult result,
        ExecutionMode mode,
        SandboxErrorCode code)
    {
        Assert.False(result.Succeeded);
        Assert.True(
            result.Error!.Code == code,
            $"Expected {code}, got {result.Error.Code}: {result.Error.SafeMessage}");
        Assert.Equal(mode, result.ActualMode);
    }

    private static string LoopQuotaModuleJson(CollectionLoopShape shape)
        => $$"""
        {
          "id": "compiled-collection-loop-quota",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{{CollectionParameter(shape)}}],
              "returnType": "I32",
              "body": [
                { "op": "set", "name": "last", "value": { "i32": 0 } },
                {
                  "op": "forRange",
                  "local": "i",
                  "start": { "i32": 0 },
                  "end": { "i32": 4 },
                  "body": [
                    {
                      "op": "set",
                      "name": "last",
                      "value": {{CollectionRead(shape, valid: true)}}
                    }
                  ]
                },
                { "op": "return", "value": { "var": "last" } }
              ]
            }
          ]
        }
        """;

    private static string MapGetMissingKeyModuleJson()
        => CollectionFailureModuleJson(
            CollectionLoopShape.MapGet,
            CollectionRead(CollectionLoopShape.MapGet, valid: false));

    private static string ListGetOutOfRangeModuleJson()
        => CollectionFailureModuleJson(
            CollectionLoopShape.ListGet,
            CollectionRead(CollectionLoopShape.ListGet, valid: false));

    private static string CollectionFailureModuleJson(CollectionLoopShape shape, string read)
        => $$"""
        {
          "id": "compiled-collection-loop-failure",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{{CollectionParameter(shape)}}],
              "returnType": "I32",
              "body": [
                { "op": "set", "name": "last", "value": { "i32": 0 } },
                {
                  "op": "forRange",
                  "local": "i",
                  "start": { "i32": 0 },
                  "end": { "i32": 4 },
                  "body": [
                    { "op": "set", "name": "last", "value": {{read}} }
                  ]
                },
                { "op": "return", "value": { "var": "last" } }
              ]
            }
          ]
        }
        """;

    private static string CollectionParameter(CollectionLoopShape shape)
        => shape switch
        {
            CollectionLoopShape.MapGet =>
                """{ "name": "scores", "type": { "name": "Map", "arguments": ["String", "I32"] } }""",
            _ => """{ "name": "items", "type": { "name": "List", "arguments": ["I32"] } }"""
        };

    private static string CollectionRead(CollectionLoopShape shape, bool valid)
        => shape switch
        {
            CollectionLoopShape.ListCount => """{ "call": "list.count", "args": [{ "var": "items" }] }""",
            CollectionLoopShape.ListGet =>
                $$"""{ "call": "list.get", "args": [{ "var": "items" }, { "i32": {{(valid ? 0 : 99)}} }] }""",
            CollectionLoopShape.MapGet =>
                $$"""{ "call": "map.get", "args": [{ "var": "scores" }, { "string": "{{(valid ? "alice" : "bob")}}" }] }""",
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };

    private static SandboxValue InputFor(CollectionLoopShape shape)
        => shape == CollectionLoopShape.MapGet
            ? SandboxValue.FromMap(
                new Dictionary<SandboxValue, SandboxValue>
                {
                    [SandboxValue.FromString("alice")] = SandboxValue.FromInt32(7)
                },
                SandboxType.String,
                SandboxType.I32)
            : SandboxValue.FromList(
                Enumerable.Range(1, 32).Select(SandboxValue.FromInt32).ToArray(),
                SandboxType.I32);

    public enum CollectionLoopShape
    {
        ListCount,
        ListGet,
        MapGet
    }
}
