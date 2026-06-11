namespace SafeIR.Tests;

public sealed class FileWritePolicyTests
{
    [Fact]
    public async Task File_write_with_create_disabled_replaces_existing_file()
    {
        using var temp = TempDirectory.Create();
        var target = Path.Combine(temp.Path, "existing.txt");
        await File.WriteAllTextAsync(target, "old");
        var result = await ExecuteWriteAsync(temp.Path, "existing.txt", "new", allowCreate: false, allowOverwrite: true);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal("new", await File.ReadAllTextAsync(target));
        Assert.Empty(Directory.GetFiles(temp.Path, "*.tmp-*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task File_write_with_create_disabled_denies_missing_target()
    {
        using var temp = TempDirectory.Create();
        var result = await ExecuteWriteAsync(temp.Path, "missing.txt", "new", allowCreate: false, allowOverwrite: true);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PermissionDenied, result.Error!.Code);
        Assert.False(File.Exists(Path.Combine(temp.Path, "missing.txt")));
        Assert.Empty(Directory.GetFiles(temp.Path, "*.tmp-*", SearchOption.AllDirectories));
    }

    private static async Task<SandboxExecutionResult> ExecuteWriteAsync(
        string root,
        string path,
        string text,
        bool allowCreate,
        bool allowOverwrite)
    {
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync(FileWriteJson(path, text));
        var policy = SandboxPolicyBuilder.Create()
            .GrantFileWrite(root, 1024, allowCreate, allowOverwrite)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);
        return await host.ExecuteAsync(plan, "main", SandboxValue.Unit);
    }

    private static string FileWriteJson(string path, string text)
        => $$"""
        {
          "id": "file-write-policy",
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
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "safe-ir-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path)) {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
