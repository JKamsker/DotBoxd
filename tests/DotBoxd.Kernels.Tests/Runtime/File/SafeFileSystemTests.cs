using DotBoxd.Kernels;

namespace DotBoxd.Kernels.Tests;

public sealed class SafeFileSystemTests
{
    [Fact]
    public async Task Granted_file_read_is_scoped_and_audited()
    {
        using var temp = TempDirectory.Create();
        Directory.CreateDirectory(Path.Combine(temp.Path, "config"));
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "config", "settings.json"), "tenant-settings");

        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(InterpreterAndPolicyTests.FileReadJson("config/settings.json"));
        var policy = FilePolicyBuilder()
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
            await host.ImportJsonAsync(InterpreterAndPolicyTests.FileReadJson(path)));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-JSON-PATH");
    }

    [Fact]
    public async Task File_read_charges_actual_streamed_bytes()
    {
        using var temp = TempDirectory.Create();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "text.txt"), "hello");
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(InterpreterAndPolicyTests.FileReadJson("text.txt"));
        var policy = FilePolicyBuilder()
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
        var module = await host.ImportJsonAsync(InterpreterAndPolicyTests.FileReadJson("text.txt"));
        var policy = FilePolicyBuilder()
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
        var module = await host.ImportJsonAsync(InterpreterAndPolicyTests.FileReadJson("text.txt"));
        var policy = FilePolicyBuilder()
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
        var module = await host.ImportJsonAsync(FileWriteJson("out/result.txt", "hello"));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-POLICY-CAP");
    }

    [Fact]
    public async Task Granted_file_write_is_scoped_atomic_and_audited()
    {
        using var temp = TempDirectory.Create();
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(FileWriteJson("result.txt", "written"));
        var policy = FilePolicyBuilder()
            .GrantFileWrite(temp.Path, 1024, allowCreate: true, allowOverwrite: false)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded);
        Assert.Equal("written", await File.ReadAllTextAsync(Path.Combine(temp.Path, "result.txt")));
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
            await host.ImportJsonAsync(FileWriteJson(path, "blocked")));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-JSON-PATH");
    }

    [Fact]
    public async Task File_write_respects_overwrite_policy()
    {
        using var temp = TempDirectory.Create();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "existing.txt"), "original");
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(FileWriteJson("existing.txt", "new"));
        var policy = FilePolicyBuilder()
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
    public async Task File_write_default_builder_grant_denies_create_and_overwrite()
    {
        using var temp = TempDirectory.Create();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "existing.txt"), "original");
        var host = SandboxTestHost.Create();
        var createModule = await host.ImportJsonAsync(FileWriteJson("missing.txt", "new"));
        var overwriteModule = await host.ImportJsonAsync(FileWriteJson("existing.txt", "new"));
        var policy = FilePolicyBuilder()
            .GrantFileWrite(temp.Path, 1024)
            .WithFuel(5_000)
            .Build();

        var createPlan = await host.PrepareAsync(createModule, policy);
        var overwritePlan = await host.PrepareAsync(overwriteModule, policy);
        var create = await host.ExecuteAsync(createPlan, "main", SandboxValue.Unit);
        var overwrite = await host.ExecuteAsync(overwritePlan, "main", SandboxValue.Unit);

        Assert.False(create.Succeeded);
        Assert.False(overwrite.Succeeded);
        Assert.Equal(SandboxErrorCode.PermissionDenied, create.Error!.Code);
        Assert.Equal(SandboxErrorCode.PermissionDenied, overwrite.Error!.Code);
        Assert.False(File.Exists(Path.Combine(temp.Path, "missing.txt")));
        Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(temp.Path, "existing.txt")));
    }

    [Fact]
    public async Task File_write_denies_nested_paths_fail_closed()
    {
        using var temp = TempDirectory.Create();
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(FileWriteJson("out/result.txt", "written"));
        var policy = FilePolicyBuilder()
            .GrantFileWrite(temp.Path, 1024, allowCreate: true, allowOverwrite: true)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PermissionDenied, result.Error!.Code);
        Assert.False(Directory.Exists(Path.Combine(temp.Path, "out")));
    }

    [Fact]
    public async Task File_write_direct_grant_without_modes_denies_create()
    {
        using var temp = TempDirectory.Create();
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(FileWriteJson("missing.txt", "new"));
        var policy = new SandboxPolicy(
            "direct-file-write",
            SandboxEffects.Pure | SandboxEffect.FileWrite | SandboxEffect.Audit,
            [
                new CapabilityGrant("file.write", new Dictionary<string, string>
                {
                    ["root"] = temp.Path,
                    ["maxBytesPerRun"] = "1024"
                })
            ],
            new ResourceLimits(MaxFuel: 5_000, MaxFileBytesWritten: 1024));
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PermissionDenied, result.Error!.Code);
        Assert.False(File.Exists(Path.Combine(temp.Path, "missing.txt")));
    }

    [Fact]
    public async Task File_write_respects_byte_quota_before_writing()
    {
        using var temp = TempDirectory.Create();
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(FileWriteJson("too-large.txt", "0123456789"));
        var policy = FilePolicyBuilder()
            .GrantFileWrite(temp.Path, maxBytesPerRun: 4, allowCreate: true, allowOverwrite: false)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        Assert.False(File.Exists(Path.Combine(temp.Path, "too-large.txt")));
    }

    [Fact]
    public async Task File_write_charges_encoded_buffer_allocation_before_writing()
    {
        using var temp = TempDirectory.Create();
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(FileWriteJson("alloc-too-large.txt", "abcde"));
        var policy = FilePolicyBuilder()
            .GrantFileWrite(temp.Path, maxBytesPerRun: 1024, allowCreate: true, allowOverwrite: false)
            .WithMaxAllocatedBytes(12)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        Assert.False(File.Exists(Path.Combine(temp.Path, "alloc-too-large.txt")));
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

    private static SandboxPolicyBuilder FilePolicyBuilder()
        => SandboxPolicyBuilder.Create().WithWallTime(TimeSpan.FromSeconds(2));

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
            if (Directory.Exists(Path)) {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
