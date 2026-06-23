using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Runtime.File;

public sealed class SafeFileSystemWriteTests
{
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
        Assert.Equal("written", await System.IO.File.ReadAllTextAsync(Path.Combine(temp.Path, "result.txt")));
        Assert.Empty(Directory.GetFiles(temp.Path, "*.tmp-*", SearchOption.AllDirectories));
        var audit = Assert.Single(result.AuditEvents, e => e.BindingId == "file.writeText" && e.Success);
        Assert.Equal("file", audit.Fields!["resourceKind"]);
        Assert.Equal("7", audit.Fields["bytesWritten"]);
    }

    [Fact]
    public async Task File_write_allows_in_root_filename_starting_with_two_dots()
    {
        using var temp = TempDirectory.Create();
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(FileWriteJson("..safe.txt", "written"));
        var policy = FilePolicyBuilder()
            .GrantFileWrite(temp.Path, 1024, allowCreate: true, allowOverwrite: false)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal("written", await System.IO.File.ReadAllTextAsync(Path.Combine(temp.Path, "..safe.txt")));
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
        await System.IO.File.WriteAllTextAsync(Path.Combine(temp.Path, "existing.txt"), "original");
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
        Assert.Equal("original", await System.IO.File.ReadAllTextAsync(Path.Combine(temp.Path, "existing.txt")));
    }

    [Fact]
    public async Task File_write_default_builder_grant_denies_create_and_overwrite()
    {
        using var temp = TempDirectory.Create();
        await System.IO.File.WriteAllTextAsync(Path.Combine(temp.Path, "existing.txt"), "original");
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
        Assert.False(System.IO.File.Exists(Path.Combine(temp.Path, "missing.txt")));
        Assert.Equal("original", await System.IO.File.ReadAllTextAsync(Path.Combine(temp.Path, "existing.txt")));
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
            SandboxEffects.Pure | SandboxEffect.FileWrite | SandboxEffect.Concurrency | SandboxEffect.Audit,
            [
                new CapabilityGrant(RuntimeCapabilityIds.Async, new Dictionary<string, string>()),
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
        Assert.False(System.IO.File.Exists(Path.Combine(temp.Path, "missing.txt")));
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
        Assert.False(System.IO.File.Exists(Path.Combine(temp.Path, "too-large.txt")));
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
        Assert.False(System.IO.File.Exists(Path.Combine(temp.Path, "alloc-too-large.txt")));
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
        => SandboxPolicyBuilder.Create()
            .AllowRuntimeAsync()
            .WithWallTime(TimeSpan.FromSeconds(2));
}
