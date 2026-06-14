namespace SafeIR.Tests;

public sealed class SafeFileConcurrencyTests
{
    [Fact]
    public async Task Concurrent_file_writes_publish_one_result_when_overwrite_is_denied()
    {
        using var temp = TempDirectory.Create();
        using var host = SandboxTestHost.Create();

        var tasks = Enumerable.Range(0, 8)
            .Select(i => ExecuteWriteAsync(host, temp.Path, "shared.txt", $"value-{i}"))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(r => r.Succeeded));
        Assert.Equal(7, results.Count(r =>
            !r.Succeeded &&
            r.Error?.Code == SandboxErrorCode.PermissionDenied &&
            r.Error.SafeMessage.Contains("overwrite", StringComparison.Ordinal)));
        var published = await File.ReadAllTextAsync(Path.Combine(temp.Path, "shared.txt"));
        Assert.Matches("^value-[0-7]$", published);
        Assert.Empty(Directory.GetFiles(temp.Path, "*.tmp-*", SearchOption.AllDirectories));
    }

    private static async Task<SandboxExecutionResult> ExecuteWriteAsync(
        Hosting.SandboxHost host,
        string root,
        string path,
        string text)
    {
        var module = await host.ImportJsonAsync(FileWriteJson(path, text));
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .GrantFileWrite(root, 1024, allowCreate: true, allowOverwrite: false)
                .WithWallTime(TimeSpan.FromSeconds(2))
                .WithFuel(5_000)
                .Build());
        return await host.ExecuteAsync(plan, "main", SandboxValue.Unit);
    }

    private static string FileWriteJson(string path, string text)
        => $$"""
        {
          "id": "file-concurrency-{{text[^1]}}",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "file.write", "reason": "concurrency" }],
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
                    "args": [{ "path": "{{path}}" }, { "string": "{{text}}" }]
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
                "safe-ir-file-concurrency-" + Guid.NewGuid().ToString("N"));
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
