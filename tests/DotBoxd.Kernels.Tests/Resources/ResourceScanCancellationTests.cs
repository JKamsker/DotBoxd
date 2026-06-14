using DotBoxd.Kernels;

namespace DotBoxd.Kernels.Tests;

public sealed class ResourceScanCancellationTests
{
    [Fact]
    public async Task Entrypoint_input_shape_scan_observes_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync("""
        {
          "id": "cancelled-shape-scan",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [
                { "name": "input", "type": { "name": "List", "arguments": ["I32"] } }
              ],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "i32": 1 } }]
            }
          ]
        }
        """);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build());
        var input = SandboxValue.FromList(
            Enumerable.Range(0, 10_000).Select(SandboxValue.FromInt32).ToArray(),
            SandboxType.I32);

        var result = await host.ExecuteAsync(
            plan,
            "main",
            input,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted },
            cancellation.Token);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.Cancelled, result.Error!.Code);
    }
}
