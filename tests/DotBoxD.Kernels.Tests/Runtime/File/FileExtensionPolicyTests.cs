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
