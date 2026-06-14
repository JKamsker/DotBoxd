using System.Text.Json;

namespace DotBoxd.Kernels.Tests;

public sealed class RuntimeFuzzCoverageTests
{
    private static readonly string[] Parameters = ["a", "b", "c"];

    [Fact]
    public async Task Generated_pure_modules_prepare_and_execute_under_deadline()
    {
        using var host = SandboxTestHost.Create();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var random = new Random(0x51AFE100);

        for (var i = 0; i < 30; i++)
        {
            var module = await host.ImportJsonAsync(PureModuleJson(i, Expression(random, 3)), cancellation.Token);
            var plan = await host.PrepareAsync(
                module,
                SandboxPolicyBuilder.Create()
                    .WithFuel(50_000)
                    .WithWallTime(TimeSpan.FromSeconds(2))
                    .Build(),
                cancellation.Token);

            var result = await host.ExecuteAsync(
                plan,
                "main",
                Input(random),
                new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted },
                cancellation.Token);

            Assert.True(result.Succeeded, $"case {i}: {result.Error?.SafeMessage}");
            Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
            Assert.IsType<I32Value>(result.Value);
        }
    }

    [Fact]
    public async Task Generated_file_modules_require_policy_then_execute_with_grant()
    {
        using var temp = TempDirectory.Create();
        using var host = SandboxTestHost.Create();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        for (var i = 0; i < 8; i++)
        {
            var path = $"data/case-{i}.txt";
            Directory.CreateDirectory(Path.Combine(temp.Path, "data"));
            await File.WriteAllTextAsync(Path.Combine(temp.Path, "data", $"case-{i}.txt"), $"value-{i}", cancellation.Token);
            var module = await host.ImportJsonAsync(FileModuleJson(i, path), cancellation.Token);

            var denied = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
                await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build(), cancellation.Token));
            Assert.Contains(denied.Diagnostics, d => d.Code == "E-POLICY-CAP");

            var plan = await host.PrepareAsync(
                module,
                SandboxPolicyBuilder.Create()
                    .GrantFileRead(temp.Path, 1024)
                    .WithFuel(10_000)
                    .WithWallTime(TimeSpan.FromSeconds(2))
                    .Build(),
                cancellation.Token);
            var result = await host.ExecuteAsync(
                plan,
                "main",
                SandboxValue.Unit,
                new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted },
                cancellation.Token);

            Assert.True(result.Succeeded, result.Error?.SafeMessage);
            Assert.Equal($"value-{i}", ((StringValue)result.Value!).Value);
            Assert.Equal(1, result.ResourceUsage.HostCalls);
            Assert.Contains(result.AuditEvents, e => e.BindingId == "file.readText" && e.Success);
        }
    }

    private static SandboxValue Input(Random random)
        => SandboxValue.FromList([
            SandboxValue.FromInt32(random.Next(-3, 4)),
            SandboxValue.FromInt32(random.Next(-3, 4)),
            SandboxValue.FromInt32(random.Next(-3, 4))
        ]);

    private static string Expression(Random random, int depth)
    {
        if (depth == 0 || random.Next(4) == 0)
        {
            return random.Next(2) == 0
                ? $$"""{ "i32": {{random.Next(-3, 4)}} }"""
                : $$"""{ "var": "{{Parameters[random.Next(Parameters.Length)]}}" }""";
        }

        return $$"""
        {
          "op": "{{Operator(random)}}",
          "left": {{Expression(random, depth - 1)}},
          "right": {{Expression(random, depth - 1)}}
        }
        """;
    }

    private static string Operator(Random random)
        => random.Next(3) switch
        {
            0 => "add",
            1 => "sub",
            _ => "mul"
        };

    private static string PureModuleJson(int index, string expression)
        => $$"""
        {
          "id": "runtime-fuzz-{{index}}",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [
                { "name": "a", "type": "I32" },
                { "name": "b", "type": "I32" },
                { "name": "c", "type": "I32" }
              ],
              "returnType": "I32",
              "body": [
                { "op": "set", "name": "acc", "value": {{expression}} },
                {
                  "op": "forRange",
                  "local": "i",
                  "start": { "i32": 0 },
                  "end": { "i32": 3 },
                  "body": [
                    {
                      "op": "set",
                      "name": "acc",
                      "value": { "op": "add", "left": { "var": "acc" }, "right": { "var": "i" } }
                    }
                  ]
                },
                { "op": "return", "value": { "var": "acc" } }
              ]
            }
          ]
        }
        """;

    private static string FileModuleJson(int index, string path)
        => $$"""
        {
          "id": "runtime-file-fuzz-{{index}}",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "file.read", "reason": "fuzz" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "String",
              "body": [
                {
                  "op": "return",
                  "value": { "call": "file.readText", "args": [{ "path": {{JsonSerializer.Serialize(path)}} }] }
                }
              ]
            }
          ]
        }
        """;

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "dotboxd-runtime-fuzz-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
