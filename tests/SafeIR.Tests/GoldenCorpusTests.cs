using SafeIR.Hosting;

namespace SafeIR.Tests;

public sealed class GoldenCorpusTests
{
    [Theory]
    [InlineData("pure-score", "cbdaa1be5d4ca6ae2c0bdbe3ca30c7b2614671754d5d0240af4dbb06c0bbeddd")]
    [InlineData("file-read", "70e12b0a9a08f9b2c5fccd2f7c52bc43439d99e7dd3cdb43907a31baf349af90")]
    public void Golden_modules_preserve_canonical_hashes(string name, string expectedHash)
    {
        var module = SafeIrJsonImporter.Import(GoldenJson(name));

        Assert.Equal(expectedHash, CanonicalModuleHasher.Hash(module));
    }

    [Fact]
    public async Task Golden_pure_score_result_is_stable_across_backends()
    {
        using var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(GoldenJson("pure-score"));
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(10_000).Build());
        var input = SandboxValue.FromList([SandboxValue.FromInt32(3), SandboxValue.FromInt32(4)]);

        var interpreted = await ExecuteAsync(host, plan, input, ExecutionMode.Interpreted);
        var compiled = await ExecuteAsync(host, plan, input, ExecutionMode.Compiled);

        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        Assert.Equal(34, ((I32Value)interpreted.Value!).Value);
        Assert.Equal(34, ((I32Value)compiled.Value!).Value);
        Assert.Equal(9, interpreted.ResourceUsage.FuelUsed);
        Assert.Equal(10, compiled.ResourceUsage.FuelUsed);
    }

    private static ValueTask<SandboxExecutionResult> ExecuteAsync(
        Hosting.SandboxHost host,
        ExecutionPlan plan,
        SandboxValue input,
        ExecutionMode mode)
        => host.ExecuteAsync(
            plan,
            "main",
            input,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });

    private static string GoldenJson(string name)
        => name switch
        {
            "pure-score" => """
            {
              "id": "golden-score",
              "version": "1.0.0",
              "targetSandboxVersion": "1.0.0",
              "functions": [
                {
                  "id": "main",
                  "visibility": "entrypoint",
                  "parameters": [
                    { "name": "level", "type": "I32" },
                    { "name": "rarity", "type": "I32" }
                  ],
                  "returnType": "I32",
                  "body": [
                    {
                      "op": "set",
                      "name": "base",
                      "value": { "op": "mul", "left": { "var": "level" }, "right": { "i32": 10 } }
                    },
                    {
                      "op": "return",
                      "value": { "op": "add", "left": { "var": "base" }, "right": { "var": "rarity" } }
                    }
                  ]
                }
              ]
            }
            """,
            "file-read" => """
            {
              "id": "golden-file-read",
              "version": "1.0.0",
              "capabilityRequests": [{ "id": "file.read", "reason": "golden" }],
              "functions": [
                {
                  "id": "main",
                  "visibility": "entrypoint",
                  "parameters": [],
                  "returnType": "String",
                  "body": [
                    {
                      "op": "return",
                      "value": { "call": "file.readText", "args": [{ "path": "settings.json" }] }
                    }
                  ]
                }
              ]
            }
            """,
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, null)
        };
}
