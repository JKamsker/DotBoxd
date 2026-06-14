using DotBoxd.Kernels;

namespace DotBoxd.Kernels.Tests;

public sealed partial class PolicyBoundaryTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("txt")]
    [InlineData(".")]
    [InlineData(".txt,")]
    [InlineData(".bad/name")]
    [InlineData(".bad name")]
    public async Task Prepare_rejects_malformed_file_grant_extensions(string value)
    {
        using var temp = TempDirectory.Create();
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(FileWriteJson("out.txt", "x"));
        var policy = new SandboxPolicy(
            "bad-file-extensions",
            SandboxEffects.Pure | SandboxEffect.FileWrite | SandboxEffect.Audit,
            [
                new CapabilityGrant("file.write", new Dictionary<string, string> {
                    ["root"] = temp.Path,
                    ["maxBytesPerRun"] = "1024",
                    ["allowCreate"] = "true",
                    ["allowOverwrite"] = "true",
                    ["allowedExtensions"] = value
                })
            ],
            new ResourceLimits(MaxFuel: 5_000, MaxFileBytesWritten: 1024));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, policy));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-POLICY-GRANT-PARAM");
    }
}
