using SafeIR;

namespace SafeIR.Tests;

public sealed class SafeFileSystemTests
{
    [Fact]
    public async Task Granted_file_read_is_scoped_and_audited()
    {
        using var temp = TempDirectory.Create();
        Directory.CreateDirectory(Path.Combine(temp.Path, "config"));
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "config", "settings.json"), "tenant-settings");

        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync(InterpreterAndPolicyTests.FileReadJson("config/settings.json"));
        var policy = SandboxPolicyBuilder.Create()
            .GrantFileRead(temp.Path, 1024)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded);
        Assert.Equal("tenant-settings", ((StringValue)result.Value!).Value);
        var audit = Assert.Single(result.AuditEvents, e => e.BindingId == "file.readText" && e.Success);
        Assert.Equal("file", audit.Fields!["resourceKind"]);
        Assert.Equal("15", audit.Fields["bytesRead"]);
    }

    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("config/../../secret.txt")]
    public async Task File_read_path_traversal_is_denied_at_import(string path)
    {
        var host = SandboxTestHost.Create();

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.ParseJsonAsync(InterpreterAndPolicyTests.FileReadJson(path)));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-JSON-PATH");
    }

    [Fact]
    public async Task File_read_charges_actual_streamed_bytes()
    {
        using var temp = TempDirectory.Create();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "text.txt"), "hello");
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync(InterpreterAndPolicyTests.FileReadJson("text.txt"));
        var policy = SandboxPolicyBuilder.Create()
            .GrantFileRead(temp.Path, 5)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(5, result.ResourceUsage.FileBytesRead);
        Assert.Contains(result.AuditEvents, e => e.BindingId == "file.readText" && e.Bytes == 5);
    }

    [Fact]
    public async Task File_read_respects_byte_quota_while_streaming()
    {
        using var temp = TempDirectory.Create();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "text.txt"), "hello");
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync(InterpreterAndPolicyTests.FileReadJson("text.txt"));
        var policy = SandboxPolicyBuilder.Create()
            .GrantFileRead(temp.Path, 4)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
    }

    [Fact]
    public async Task File_read_respects_allocation_quota_while_streaming()
    {
        using var temp = TempDirectory.Create();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "text.txt"), "hello");
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync(InterpreterAndPolicyTests.FileReadJson("text.txt"));
        var policy = SandboxPolicyBuilder.Create()
            .GrantFileRead(temp.Path, 1024)
            .WithMaxAllocatedBytes(4)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
    }

    [Fact]
    public async Task File_write_is_denied_without_host_grant()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync(FileWriteJson("out/result.txt", "hello"));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-POLICY-CAP");
    }

    [Fact]
    public async Task Granted_file_write_is_scoped_atomic_and_audited()
    {
        using var temp = TempDirectory.Create();
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync(FileWriteJson("out/result.txt", "written"));
        var policy = SandboxPolicyBuilder.Create()
            .GrantFileWrite(temp.Path, 1024)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded);
        Assert.Equal("written", await File.ReadAllTextAsync(Path.Combine(temp.Path, "out", "result.txt")));
        Assert.Empty(Directory.GetFiles(temp.Path, "*.tmp-*", SearchOption.AllDirectories));
        var audit = Assert.Single(result.AuditEvents, e => e.BindingId == "file.writeText" && e.Success);
        Assert.Equal("file", audit.Fields!["resourceKind"]);
        Assert.Equal("7", audit.Fields["bytesWritten"]);
    }

    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("config/../../secret.txt")]
    public async Task File_write_path_traversal_is_denied_at_import(string path)
    {
        var host = SandboxTestHost.Create();

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.ParseJsonAsync(FileWriteJson(path, "blocked")));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-JSON-PATH");
    }

    [Fact]
    public async Task File_write_respects_overwrite_policy()
    {
        using var temp = TempDirectory.Create();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "existing.txt"), "original");
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync(FileWriteJson("existing.txt", "new"));
        var policy = SandboxPolicyBuilder.Create()
            .GrantFileWrite(temp.Path, 1024, allowOverwrite: false)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PermissionDenied, result.Error!.Code);
        Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(temp.Path, "existing.txt")));
    }

    [Fact]
    public async Task File_write_respects_byte_quota_before_writing()
    {
        using var temp = TempDirectory.Create();
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync(FileWriteJson("too-large.txt", "0123456789"));
        var policy = SandboxPolicyBuilder.Create()
            .GrantFileWrite(temp.Path, maxBytesPerRun: 4)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        Assert.False(File.Exists(Path.Combine(temp.Path, "too-large.txt")));
    }

    private static string FileWriteJson(string path, string text)
        => $$"""
        {
          "id": "file-writer",
          "version": "1.0.0",
          "capabilityRequests": [
            { "id": "file.write", "reason": "test write" }
          ],
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
