using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Tests.Interpreter;

namespace DotBoxD.Kernels.Tests.Policy;

public sealed class PolicyFileGrantValidationTests
{
    [Fact]
    public void File_grants_do_not_implicitly_grant_runtime_async()
    {
        var root = Path.GetTempPath();
        var readPolicy = SandboxPolicyBuilder.Create().GrantFileRead(root, 1024).Build();
        var writePolicy = SandboxPolicyBuilder.Create()
            .GrantFileWrite(root, 1024, allowCreate: true, allowOverwrite: true)
            .Build();

        Assert.DoesNotContain(readPolicy.Grants, grant => grant.Id == RuntimeCapabilityIds.Async);
        Assert.DoesNotContain(writePolicy.Grants, grant => grant.Id == RuntimeCapabilityIds.Async);
        Assert.False(readPolicy.AllowedEffects.HasFlag(SandboxEffect.Concurrency));
        Assert.False(writePolicy.AllowedEffects.HasFlag(SandboxEffect.Concurrency));
    }

    [Fact]
    public void File_grant_builder_rejects_relative_root()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            SandboxPolicyBuilder.Create().GrantFileRead("relative/config", 1024));

        Assert.Equal("root", ex.ParamName);
    }

    [Fact]
    public void File_grant_builder_allows_missing_root_directory()
    {
        var root = Path.Combine(Path.GetTempPath(), "dotboxd-missing-" + Guid.NewGuid().ToString("N"));

        var readPolicy = SandboxPolicyBuilder.Create().GrantFileRead(root, 1024).Build();
        var writePolicy = SandboxPolicyBuilder.Create().GrantFileWrite(root, 1024).Build();

        Assert.Contains(readPolicy.Grants, grant =>
            grant.Id == "file.read" &&
            string.Equals(grant.Parameters["root"], root, StringComparison.Ordinal));
        Assert.Contains(writePolicy.Grants, grant =>
            grant.Id == "file.write" &&
            string.Equals(grant.Parameters["root"], root, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Prepare_rejects_direct_file_grant_with_relative_root()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(InterpreterAndPolicyTests.FileReadJson("settings.json"));
        var policy = new SandboxPolicy(
            "relative-root",
            SandboxEffects.Pure | SandboxEffect.FileRead,
            [
                new CapabilityGrant(
                    "file.read",
                    new Dictionary<string, string>
                    {
                        ["root"] = "relative/config",
                        ["maxBytesPerRun"] = "1024"
                    })
            ],
            new ResourceLimits(MaxFuel: 1_000, MaxFileBytesRead: 1024));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, policy));

        Assert.Contains(ex.Diagnostics, d =>
            d.Code == "E-POLICY-GRANT-PARAM" &&
            d.Message.Contains("absolute canonical", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Prepare_rejects_direct_file_grant_with_missing_root_directory()
    {
        var root = Path.Combine(Path.GetTempPath(), "dotboxd-missing-" + Guid.NewGuid().ToString("N"));

        var ex = await PrepareWithDirectReadRootAsync(root);

        Assert.Contains(ex.Diagnostics, d =>
            d.Code == "E-POLICY-GRANT-PARAM" &&
            d.Message.Contains("existing directory", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Prepare_rejects_direct_file_grant_with_file_root()
    {
        var root = Path.GetTempFileName();
        try
        {
            var ex = await PrepareWithDirectReadRootAsync(root);

            Assert.Contains(ex.Diagnostics, d =>
                d.Code == "E-POLICY-GRANT-PARAM" &&
                d.Message.Contains("existing directory", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(root);
        }
    }

    [Fact]
    public async Task Prepare_rejects_wildcard_file_read_grant_with_relative_root()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(InterpreterAndPolicyTests.FileReadJson("settings.json"));
        var policy = new SandboxPolicy(
            "relative-wildcard-root",
            SandboxEffects.Pure | SandboxEffect.FileRead | SandboxEffect.Concurrency,
            [
                new CapabilityGrant(RuntimeCapabilityIds.Async, new Dictionary<string, string>()),
                new CapabilityGrant(
                    "file.*",
                    new Dictionary<string, string>
                    {
                        ["root"] = "relative/config",
                        ["maxBytesPerRun"] = "1024"
                    })
            ],
            new ResourceLimits(MaxFuel: 1_000, MaxFileBytesRead: 1024));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, policy));

        Assert.Contains(ex.Diagnostics, d =>
            d.Code == "E-POLICY-GRANT-PARAM" &&
            d.Message.Contains("absolute canonical", StringComparison.Ordinal));
    }

    private static async Task<SandboxValidationException> PrepareWithDirectReadRootAsync(string root)
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(InterpreterAndPolicyTests.FileReadJson("settings.json"));
        var policy = new SandboxPolicy(
            "bad-file-root",
            SandboxEffects.Pure | SandboxEffect.FileRead,
            [
                new CapabilityGrant(
                    "file.read",
                    new Dictionary<string, string>
                    {
                        ["root"] = root,
                        ["maxBytesPerRun"] = "1024"
                    })
            ],
            new ResourceLimits(MaxFuel: 1_000, MaxFileBytesRead: 1024));

        return await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, policy));
    }
}
