namespace SafeIR.Tests;

public sealed class SafeFileSystemReparseRaceTests
{
    [Fact]
    public async Task File_write_denies_parent_swap_before_temp_create()
    {
        using var root = TempDirectory.Create();
        using var outside = TempDirectory.Create();
        var result = await ExecuteWriteAsync(root.Path, "safe/nested/out.txt", "new");

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PermissionDenied, result.Error!.Code);
        Assert.False(File.Exists(Path.Combine(outside.Path, "out.txt")));
    }

    [Fact]
    public async Task File_write_parent_creation_does_not_create_below_swapped_reparse_point()
    {
        using var root = TempDirectory.Create();
        using var outside = TempDirectory.Create();
        var result = await ExecuteWriteAsync(root.Path, "a/b/out.txt", "new");

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PermissionDenied, result.Error!.Code);
        Assert.False(Directory.Exists(Path.Combine(outside.Path, "b")));
    }

    private static async Task<SandboxExecutionResult> ExecuteWriteAsync(string root, string path, string text)
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(FileWriteJson(path, text));
        var policy = SandboxPolicyBuilder.Create()
            .WithWallTime(TimeSpan.FromSeconds(2))
            .GrantFileWrite(root, 1024, allowCreate: true, allowOverwrite: true)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);
        return await host.ExecuteAsync(plan, "main", SandboxValue.Unit);
    }

    private static string FileWriteJson(string path, string text)
        => $$"""
        {
          "id": "file-writer-race",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "file.write", "reason": "test write" }],
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
                      { "path": "{{path.Replace("\\", "\\\\", StringComparison.Ordinal)}}" },
                      { "string": "{{text}}" }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;

    private static bool TryCreateDirectoryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static void TryDeleteDirectoryLink(string link)
    {
        try
        {
            if (Directory.Exists(link))
            {
                Directory.Delete(link);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "safe-ir-race-" + Guid.NewGuid().ToString("N"));
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
