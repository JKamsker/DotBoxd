using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Compiled.SideEffectParity;

internal static class FileReadParityTestSupport
{
    public static FileReadParityTempDirectory CreateTempDirectory()
        => FileReadParityTempDirectory.Create();

    public static SandboxHost CreateHost()
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddFileBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

    public static async Task<SandboxExecutionResult> RunAsync(
        string root,
        string relativePath,
        ExecutionMode mode)
    {
        var host = CreateHost();
        var module = await host.ImportJsonAsync(ModuleJson("parity-file-read", relativePath));
        var policy = SandboxPolicyBuilder.Create()
            .AllowRuntimeAsync()
            .GrantFileRead(root, maxBytesPerRun: 65_536)
            .WithFuel(10_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        return await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });
    }

    public static string ModuleJson(string id, string relativePath)
        => $$"""
        {
          "id": "{{id}}",
          "version": "1.0.0",
          "capabilityRequests": [
            { "id": "file.read", "reason": "parity test read" }
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
                    "args": [{ "path": "{{EscapeJsonString(relativePath)}}" }]
                  }
                }
              ]
            }
          ]
        }
        """;

    private static string EscapeJsonString(string value)
        => value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
}

internal sealed class FileReadParityTempDirectory : IDisposable
{
    private FileReadParityTempDirectory(string path) => Path = path;

    public string Path { get; }

    public static FileReadParityTempDirectory Create()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "dotboxd-fileread-parity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return new FileReadParityTempDirectory(path);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
