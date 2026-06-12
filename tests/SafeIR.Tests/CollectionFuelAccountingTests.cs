namespace SafeIR.Tests;

public sealed class CollectionFuelAccountingTests
{
    public static TheoryData<ExecutionMode> Modes()
        => new()
        {
            ExecutionMode.Interpreted,
            ExecutionMode.Compiled
        };

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task List_add_charges_source_size_fuel(ExecutionMode mode)
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ParseJsonAsync("""
        {
          "id": "collection-fuel",
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
                    "call": "list.count",
                    "args": [
                      {
                        "call": "list.add",
                        "args": [{ "var": "items" }, { "i32": 999 }]
                      }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """);
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .WithFuel(10_000)
                .Build());
        var input = SandboxValue.FromList(
            Enumerable.Range(0, 80).Select(SandboxValue.FromInt32).ToArray(),
            SandboxType.I32);

        var result = await host.ExecuteAsync(
            plan,
            "main",
            input,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(81, ((I32Value)result.Value!).Value);
        Assert.True(result.ResourceUsage.FuelUsed >= 80);
        Assert.Equal(mode, result.ActualMode);
    }
}
