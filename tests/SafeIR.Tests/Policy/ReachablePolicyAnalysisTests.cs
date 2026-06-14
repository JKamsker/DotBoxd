using SafeIR;

namespace SafeIR.Tests;

public sealed class ReachablePolicyAnalysisTests
{
    [Fact]
    public async Task Private_unreachable_external_binding_does_not_require_policy_grant()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(ModuleWithPrivateFileRead(privateIsEntrypoint: false));

        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(1, ((I32Value)result.Value!).Value);
    }

    [Fact]
    public async Task Entrypoint_external_binding_still_requires_policy_grant()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(ModuleWithPrivateFileRead(privateIsEntrypoint: true));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-POLICY-CAP");
    }

    private static string ModuleWithPrivateFileRead(bool privateIsEntrypoint)
    {
        var visibility = privateIsEntrypoint ? "entrypoint" : "private";
        return $$"""
        {
          "id": "reachable-policy-analysis",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "i32": 1 } }]
            },
            {
              "id": "deadFileRead",
              "visibility": "{{visibility}}",
              "parameters": [],
              "returnType": "String",
              "body": [
                {
                  "op": "return",
                  "value": {
                    "call": "file.readText",
                    "args": [{ "path": "secret.txt" }]
                  }
                }
              ]
            }
          ]
        }
        """;
    }
}
