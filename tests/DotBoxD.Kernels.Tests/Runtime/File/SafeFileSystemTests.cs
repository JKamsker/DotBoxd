using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Tests.Interpreter;

namespace DotBoxD.Kernels.Tests.Runtime.File;

public sealed class SafeFileSystemTests
{
    [Fact]
    public async Task Granted_file_read_is_scoped_and_audited()
    {
        using var temp = TempDirectory.Create();
        Directory.CreateDirectory(Path.Combine(temp.Path, "config"));
        await System.IO.File.WriteAllTextAsync(Path.Combine(temp.Path, "config", "settings.json"), "tenant-settings");

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
        await System.IO.File.WriteAllTextAsync(Path.Combine(temp.Path, "text.txt"), "hello");
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
    public async Task File_read_allows_in_root_filename_starting_with_two_dots()
    {
        using var temp = TempDirectory.Create();
        await System.IO.File.WriteAllTextAsync(Path.Combine(temp.Path, "..safe.txt"), "safe");
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(InterpreterAndPolicyTests.FileReadJson("..safe.txt"));
        var policy = FilePolicyBuilder()
            .GrantFileRead(temp.Path, 1024)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal("safe", ((StringValue)result.Value!).Value);
    }

    [Fact]
    public async Task File_read_respects_byte_quota_while_streaming()
    {
        using var temp = TempDirectory.Create();
        await System.IO.File.WriteAllTextAsync(Path.Combine(temp.Path, "text.txt"), "hello");
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
        await System.IO.File.WriteAllTextAsync(Path.Combine(temp.Path, "text.txt"), "hello");
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
    public async Task File_read_allocation_failure_audits_streamed_bytes()
    {
        using var temp = TempDirectory.Create();
        var text = new string('x', 600);
        await System.IO.File.WriteAllTextAsync(Path.Combine(temp.Path, "text.txt"), text);
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(InterpreterAndPolicyTests.FileReadJson("text.txt"));
        var policy = FilePolicyBuilder()
            .GrantFileRead(temp.Path, 1024)
            .WithMaxAllocatedBytes(500)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        Assert.Equal(600, result.ResourceUsage.FileBytesRead);
        var audit = Assert.Single(result.AuditEvents, e => e.BindingId == "file.readText" && !e.Success);
        Assert.Equal(600, audit.Bytes);
        Assert.Equal("600", audit.Fields!["bytesRead"]);
    }

    private static SandboxPolicyBuilder FilePolicyBuilder()
        => SandboxPolicyBuilder.Create()
            .AllowRuntimeAsync()
            .WithWallTime(TimeSpan.FromSeconds(2));
}
