using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Tests.Interpreter;

namespace DotBoxD.Kernels.Tests.Runtime.File;

public sealed class FileExtensionPolicyTests
{
    [Fact]
    public async Task File_read_allows_configured_extension()
    {
        using var temp = TempDirectory.Create();
        await System.IO.File.WriteAllTextAsync(Path.Combine(temp.Path, "settings.json"), "ok");

        var result = await ExecuteReadAsync(temp.Path, "settings.json", ".json");

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal("ok", ((StringValue)result.Value!).Value);
    }

    [Fact]
    public async Task File_read_denies_disallowed_extension()
    {
        using var temp = TempDirectory.Create();
        await System.IO.File.WriteAllTextAsync(Path.Combine(temp.Path, "secret.txt"), "blocked");

        var result = await ExecuteReadAsync(temp.Path, "secret.txt", ".json");

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PermissionDenied, result.Error!.Code);
    }

    [Fact]
    public async Task File_write_denies_disallowed_extension_with_write_operation_message()
    {
        using var temp = TempDirectory.Create();

        var result = await ExecuteWriteAsync(temp.Path, "blocked.txt", "blocked", ".json");

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PermissionDenied, result.Error!.Code);
        Assert.Contains("file.writeText denied", result.Error.SafeMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("file.readText denied", result.Error.SafeMessage, StringComparison.Ordinal);
        Assert.False(System.IO.File.Exists(Path.Combine(temp.Path, "blocked.txt")));
    }

    private static async Task<SandboxExecutionResult> ExecuteReadAsync(
        string root,
        string path,
        string allowedExtensions)
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(InterpreterAndPolicyTests.FileReadJson(path));
        var policy = SandboxPolicyBuilder.Create()
            .WithWallTime(TimeSpan.FromSeconds(2))
            .AllowRuntimeAsync()
            .Grant(
                "file.read",
                new Dictionary<string, string>
                {
                    ["root"] = root,
                    ["maxBytesPerRun"] = "1024",
                    ["allowedExtensions"] = allowedExtensions
                },
                SandboxEffect.FileRead,
                limits => limits with { MaxFileBytesRead = 1024 })
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);
        return await host.ExecuteAsync(plan, "main", SandboxValue.Unit);
    }

    private static async Task<SandboxExecutionResult> ExecuteWriteAsync(
        string root,
        string path,
        string text,
        string allowedExtensions)
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(FileWriteJson(path, text));
        var policy = SandboxPolicyBuilder.Create()
            .WithWallTime(TimeSpan.FromSeconds(2))
            .AllowRuntimeAsync()
            .Grant(
                "file.write",
                new Dictionary<string, string>
                {
                    ["root"] = root,
                    ["allowCreate"] = "true",
                    ["allowOverwrite"] = "false",
                    ["maxBytesPerRun"] = "1024",
                    ["allowedExtensions"] = allowedExtensions
                },
                SandboxEffect.FileWrite | SandboxEffect.Audit,
                limits => limits with { MaxFileBytesWritten = 1024 })
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);
        return await host.ExecuteAsync(plan, "main", SandboxValue.Unit);
    }

    private static string FileWriteJson(string path, string text)
        => $$"""
        {
          "id": "file-write-extension-policy",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "file.write" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Unit",
              "body": [
                {
                  "op": "return",
                  "value": {
                    "call": "file.writeText",
                    "args": [
                      { "path": "{{path}}" },
                      { "string": "{{text}}" }
                    ]
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
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dotboxd-" + Guid.NewGuid().ToString("N"));
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
