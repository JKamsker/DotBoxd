using SafeIR;

namespace SafeIR.Tests;

public sealed class InterpreterAndPolicyTests
{
    [Fact]
    public async Task Pure_json_module_executes_interpreted()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(3), SandboxValue.FromInt32(2)]),
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        Assert.True(result.Succeeded);
        Assert.Equal(80, ((I32Value)result.Value!).Value);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
        Assert.Contains(result.AuditEvents, e => e.Kind == "RunSummary" && e.Success);
    }

    [Fact]
    public async Task File_read_is_denied_without_host_grant()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync(FileReadJson("config.json"));
        var policy = SandboxPolicyBuilder.Create().Build();

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () => await host.PrepareAsync(module, policy));
        Assert.Contains(ex.Diagnostics, d => d.Code == "E-POLICY-CAP");
    }

    [Fact]
    public async Task Fuel_exhaustion_stops_infinite_loop()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync("""
        {
          "id": "loop",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                {
                  "op": "while",
                  "condition": { "bool": true },
                  "body": [
                    { "op": "set", "name": "x", "value": { "i32": 1 } }
                  ]
                },
                { "op": "return", "value": { "i32": 0 } }
              ]
            }
          ]
        }
        """);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(50).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
    }

    [Fact]
    public async Task Interpreted_debug_trace_reports_ir_statement_kinds()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(3), SandboxValue.FromInt32(2)]),
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted, EnableDebugTrace = true });

        Assert.True(result.Succeeded);
        Assert.Contains(result.AuditEvents, e =>
            e.Kind == "DebugTrace" &&
            e.Message?.Contains($"node=statement:{nameof(AssignmentStatement)}", StringComparison.Ordinal) == true &&
            e.Message.Contains("function=main", StringComparison.Ordinal) &&
            e.Message.Contains($"moduleHash={plan.ModuleHash}", StringComparison.Ordinal));
        Assert.Contains(result.AuditEvents, e =>
            e.Kind == "DebugTrace" &&
            e.Message?.Contains($"node=expression:{nameof(BinaryExpression)}", StringComparison.Ordinal) == true &&
            e.Message.Contains("fuelRemaining=", StringComparison.Ordinal));
        Assert.DoesNotContain(result.AuditEvents, e =>
            e.Kind == "DebugTrace" &&
            e.Message?.Contains("Bytecode", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Interpreted_debug_trace_reports_host_binding_calls()
    {
        using var temp = TempDirectory.Create();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "config.json"), "trace-me");
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync(FileReadJson("config.json"));
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .GrantFileRead(temp.Path, maxBytesPerRun: 1024)
                .WithFuel(1_000)
                .Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted, EnableDebugTrace = true });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Contains(result.AuditEvents, e =>
            e.Kind == "DebugTrace" &&
            e.BindingId == "file.readText" &&
            e.CapabilityId == "file.read" &&
            e.Effect.HasFlag(SandboxEffect.FileRead) &&
            e.Message?.Contains($"moduleHash={plan.ModuleHash}", StringComparison.Ordinal) == true &&
            e.Message.Contains("hostCall=file.readText", StringComparison.Ordinal) &&
            e.Message.Contains("fuelRemaining=", StringComparison.Ordinal));
    }

    internal static string FileReadJson(string path)
        => $$"""
        {
          "id": "file-reader",
          "version": "1.0.0",
          "capabilityRequests": [
            { "id": "file.read", "reason": "test read" }
          ],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "String",
              "body": [
                {
                  "op": "return",
                  "value": {
                    "call": "file.readText",
                    "args": [{ "path": "{{path.Replace("\\", "\\\\", StringComparison.Ordinal)}}" }]
                  }
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
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "safe-ir-trace-" + Guid.NewGuid().ToString("N"));
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
