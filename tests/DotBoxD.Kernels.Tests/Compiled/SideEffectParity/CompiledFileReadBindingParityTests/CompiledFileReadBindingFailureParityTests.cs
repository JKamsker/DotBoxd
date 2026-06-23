using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;

namespace DotBoxD.Kernels.Tests.Compiled.SideEffectParity;

public sealed class CompiledFileReadBindingFailureParityTests
{
    [Fact]
    public async Task FileReadText_without_capability_grant_throws_SandboxValidationException_at_prepare()
    {
        var host = FileReadParityTestSupport.CreateHost();
        var module = await host.ImportJsonAsync(
            FileReadParityTestSupport.ModuleJson("parity-file-read-nocap", "cap.txt"));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await host.PrepareAsync(
                module,
                SandboxPolicyBuilder.Create().WithFuel(1_000).Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-POLICY-CAP");
    }

    [Fact]
    public async Task FileReadText_missing_file_error_code_matches_between_interpreted_and_compiled()
    {
        using var temp = FileReadParityTestSupport.CreateTempDirectory();

        var interp = await FileReadParityTestSupport.RunAsync(temp.Path, "missing.txt", ExecutionMode.Interpreted);
        var comp = await FileReadParityTestSupport.RunAsync(temp.Path, "missing.txt", ExecutionMode.Compiled);

        Assert.False(interp.Succeeded);
        Assert.False(comp.Succeeded);
        Assert.Equal(SandboxErrorCode.NotFound, interp.Error!.Code);
        Assert.Equal(SandboxErrorCode.NotFound, comp.Error!.Code);
        Assert.Equal(interp.Error.Code, comp.Error.Code);
        Assert.Equal(ExecutionMode.Compiled, comp.ActualMode);
    }

    [Fact]
    public async Task FileReadText_missing_file_failure_audit_event_matches_between_modes()
    {
        using var temp = FileReadParityTestSupport.CreateTempDirectory();

        var interp = await FileReadParityTestSupport.RunAsync(temp.Path, "gone.txt", ExecutionMode.Interpreted);
        var comp = await FileReadParityTestSupport.RunAsync(temp.Path, "gone.txt", ExecutionMode.Compiled);

        Assert.False(interp.Succeeded);
        Assert.False(comp.Succeeded);
        Assert.Equal(ExecutionMode.Compiled, comp.ActualMode);

        var ia = Assert.Single(interp.AuditEvents, e => e.Kind == "BindingCall" && e.BindingId == "file.readText");
        var ca = Assert.Single(comp.AuditEvents, e => e.Kind == "BindingCall" && e.BindingId == "file.readText");

        Assert.False(ia.Success);
        Assert.False(ca.Success);
        Assert.Equal(ia.ErrorCode, ca.ErrorCode);
        Assert.Equal(SandboxErrorCode.NotFound, ca.ErrorCode);
        Assert.Equal(ia.BindingId, ca.BindingId);
        Assert.Equal(ia.CapabilityId, ca.CapabilityId);
        Assert.Equal(ia.Effect, ca.Effect);
        Assert.Equal(ia.ResourceId, ca.ResourceId);
        Assert.Equal(ia.Fields!["resourceKind"], ca.Fields!["resourceKind"]);
        Assert.Equal("file", ca.Fields["resourceKind"]);
    }
}
