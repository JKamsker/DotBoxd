namespace SafeIR.Tests;

public sealed class SafeFileWriteUnixModeTests
{
    [Fact]
    public async Task Granted_file_write_publishes_owner_readable_unix_file()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = TempDirectory.Create();
        using var host = SandboxTestHost.Create();
        var target = Path.Combine(temp.Path, "result.txt");
        var module = await host.ImportJsonAsync(FileWriteJson("result.txt", "written"));
        var policy = SandboxPolicyBuilder.Create()
            .GrantFileWrite(temp.Path, 1024, allowCreate: true, allowOverwrite: false)
            .WithFuel(5_000)
            .WithWallTime(TimeSpan.FromSeconds(2))
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal("written", await File.ReadAllTextAsync(target));
        var mode = File.GetUnixFileMode(target);
        Assert.True((mode & UnixFileMode.UserRead) != 0, $"mode was {mode}");
        Assert.True((mode & UnixFileMode.UserWrite) != 0, $"mode was {mode}");
    }

    private static string FileWriteJson(string path, string text)
        => $$"""
        {
          "id": "file-writer-unix-mode",
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
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "safe-ir-unix-mode-" + Guid.NewGuid().ToString("N"));
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
