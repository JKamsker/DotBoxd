using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Compiled.SideEffectParity;

/// <summary>
/// Differential / parity tests for file.readText on the compiled side-effecting binding path.
///
/// file.readText carries SandboxEffect.Cpu | SandboxEffect.Alloc | SandboxEffect.FileRead,
/// capability "file.read", AuditLevel.PerResource, BindingSafety.ReadOnlyExternal, and returns
/// a String value. PR #27 supplies a CompiledBinding.RuntimeStub so that this binding compiles
/// instead of falling back to the interpreter.
///
/// Every test here runs the SAME module interpreted AND compiled with AllowFallbackToInterpreter=false
/// and asserts that every observable is identical between the two paths:
///   - result value (the text contents of the file)
///   - Succeeded, Error.Code
///   - ActualMode == Compiled for the compiled run (proves the compiled path executed)
///   - AuditEvent Kind, BindingId, CapabilityId, Effect, ResourceId, Success, Bytes, Fields["resourceKind"]
///   - ResourceUsage.HostCalls, ResourceUsage.FileBytesRead parity
///
/// Timestamp and durationMs are excluded because they are inherently wall-clock-dependent.
/// bytesRead Fields key is compared by value, not as a wall-clock derivative.
///
/// The ActualMode==Compiled assertion in every compiled run is the base-branch probe:
/// if this fails with "compiled mode supports pure modules only", the worktree is on
/// a base that predates PR #27 and compiledConfirmed should be reported false.
/// </summary>
public sealed class CompiledFileReadBindingParityTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Happy-path: value + mode parity
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FileReadText_compiled_returns_same_string_as_interpreted()
    {
        // Arrange
        const string fileContents = "hello from the compiled file read parity test";
        using var temp = FileReadParityTestSupport.CreateTempDirectory();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "read-me.txt"), fileContents);

        // Act
        var interp = await FileReadParityTestSupport.RunAsync(temp.Path, "read-me.txt", ExecutionMode.Interpreted);
        var comp = await FileReadParityTestSupport.RunAsync(temp.Path, "read-me.txt", ExecutionMode.Compiled);

        // Assert
        Assert.True(interp.Succeeded, interp.Error?.SafeMessage);
        Assert.True(comp.Succeeded, comp.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, interp.ActualMode);
        Assert.Equal(ExecutionMode.Compiled, comp.ActualMode);   // proves PR #27 base

        var interpText = ((StringValue)interp.Value!).Value;
        var compText = ((StringValue)comp.Value!).Value;
        Assert.Equal(fileContents, interpText);
        Assert.Equal(fileContents, compText);
        Assert.Equal(interpText, compText);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Audit field parity
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FileReadText_compiled_audit_fields_match_interpreted_field_for_field()
    {
        // Arrange — use a known safe filename so ResourceId is not redacted by path-segment sanitizer
        const string fileContents = "parity-audit-content";
        using var temp = FileReadParityTestSupport.CreateTempDirectory();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "data.txt"), fileContents);

        // Act
        var interp = await FileReadParityTestSupport.RunAsync(temp.Path, "data.txt", ExecutionMode.Interpreted);
        var comp = await FileReadParityTestSupport.RunAsync(temp.Path, "data.txt", ExecutionMode.Compiled);

        Assert.True(interp.Succeeded, interp.Error?.SafeMessage);
        Assert.True(comp.Succeeded, comp.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, interp.ActualMode);
        Assert.Equal(ExecutionMode.Compiled, comp.ActualMode);   // proves PR #27 base

        // Audit event: field-for-field parity
        var ia = Assert.Single(interp.AuditEvents, e => e.Kind == "BindingCall" && e.BindingId == "file.readText");
        var ca = Assert.Single(comp.AuditEvents, e => e.Kind == "BindingCall" && e.BindingId == "file.readText");

        Assert.Equal(ia.Kind, ca.Kind);
        Assert.Equal(ia.BindingId, ca.BindingId);
        Assert.Equal(ia.CapabilityId, ca.CapabilityId);
        Assert.Equal(ia.Effect, ca.Effect);
        Assert.Equal(ia.ResourceId, ca.ResourceId);
        Assert.Equal(ia.Success, ca.Success);
        Assert.Equal(ia.Bytes, ca.Bytes);
        Assert.Equal(ia.Fields!["resourceKind"], ca.Fields!["resourceKind"]);

        // Concrete expected values pinned from the binding implementation
        Assert.Equal("file.readText", ca.BindingId);
        Assert.Equal("file.read", ca.CapabilityId);
        Assert.Equal(SandboxEffect.FileRead, ca.Effect);
        Assert.True(ca.Success);
        Assert.Equal("file", ca.Fields["resourceKind"]);
        // ResourceId = sandbox://file.read/<relative-path> (from SafeFileSystem.ResolvePath)
        Assert.Equal("sandbox://file.read/data.txt", ca.ResourceId);
        // Bytes on success = byte length of the file content (UTF-8)
        Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(fileContents), (int)ca.Bytes!.Value);
    }

    [Fact]
    public async Task FileReadText_compiled_audit_bytesRead_field_present_and_matches_interpreted()
    {
        const string fileContents = "bytes-field-test-content";
        using var temp = FileReadParityTestSupport.CreateTempDirectory();
        // Default UTF-8 (no BOM) so the on-disk byte count equals Encoding.UTF8.GetByteCount(contents).
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "measure.txt"), fileContents);

        var interp = await FileReadParityTestSupport.RunAsync(temp.Path, "measure.txt", ExecutionMode.Interpreted);
        var comp = await FileReadParityTestSupport.RunAsync(temp.Path, "measure.txt", ExecutionMode.Compiled);

        Assert.True(interp.Succeeded, interp.Error?.SafeMessage);
        Assert.True(comp.Succeeded, comp.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, comp.ActualMode);   // proves PR #27 base

        var ia = Assert.Single(interp.AuditEvents, e => e.Kind == "BindingCall" && e.BindingId == "file.readText");
        var ca = Assert.Single(comp.AuditEvents, e => e.Kind == "BindingCall" && e.BindingId == "file.readText");

        // bytesRead field must be present in both modes and equal
        Assert.NotNull(ia.Fields);
        Assert.NotNull(ca.Fields);
        Assert.True(ia.Fields.ContainsKey("bytesRead"), "interpreted audit must have bytesRead field");
        Assert.True(ca.Fields.ContainsKey("bytesRead"), "compiled audit must have bytesRead field");
        Assert.Equal(ia.Fields["bytesRead"], ca.Fields["bytesRead"]);
        Assert.Equal(
            System.Text.Encoding.UTF8.GetByteCount(fileContents).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ca.Fields["bytesRead"]);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ResourceUsage parity
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FileReadText_compiled_ResourceUsage_HostCalls_matches_interpreted()
    {
        using var temp = FileReadParityTestSupport.CreateTempDirectory();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "hostcalls.txt"), "hostcalls-content");

        var interp = await FileReadParityTestSupport.RunAsync(temp.Path, "hostcalls.txt", ExecutionMode.Interpreted);
        var comp = await FileReadParityTestSupport.RunAsync(temp.Path, "hostcalls.txt", ExecutionMode.Compiled);

        Assert.True(interp.Succeeded, interp.Error?.SafeMessage);
        Assert.True(comp.Succeeded, comp.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, comp.ActualMode);   // proves PR #27 base

        Assert.Equal(interp.ResourceUsage.HostCalls, comp.ResourceUsage.HostCalls);
        Assert.Equal(1, comp.ResourceUsage.HostCalls);
    }

    [Fact]
    public async Task FileReadText_compiled_FileBytesRead_matches_interpreted()
    {
        const string fileContents = "abc-bytes-parity";
        using var temp = FileReadParityTestSupport.CreateTempDirectory();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "bytes.txt"), fileContents);

        var interp = await FileReadParityTestSupport.RunAsync(temp.Path, "bytes.txt", ExecutionMode.Interpreted);
        var comp = await FileReadParityTestSupport.RunAsync(temp.Path, "bytes.txt", ExecutionMode.Compiled);

        Assert.True(interp.Succeeded, interp.Error?.SafeMessage);
        Assert.True(comp.Succeeded, comp.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, comp.ActualMode);   // proves PR #27 base

        Assert.Equal(interp.ResourceUsage.FileBytesRead, comp.ResourceUsage.FileBytesRead);
        Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(fileContents), comp.ResourceUsage.FileBytesRead);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Unicode / multibyte content
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FileReadText_compiled_unicode_content_matches_interpreted()
    {
        // Multibyte UTF-8 characters — verifies byte counting and string round-trip
        const string fileContents = "Hello café 世界";  // "Hello café 世界"
        using var temp = FileReadParityTestSupport.CreateTempDirectory();
        // Default UTF-8 (no BOM) so the read-back equals the original string exactly.
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "unicode.txt"), fileContents);

        var interp = await FileReadParityTestSupport.RunAsync(temp.Path, "unicode.txt", ExecutionMode.Interpreted);
        var comp = await FileReadParityTestSupport.RunAsync(temp.Path, "unicode.txt", ExecutionMode.Compiled);

        Assert.True(interp.Succeeded, interp.Error?.SafeMessage);
        Assert.True(comp.Succeeded, comp.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, comp.ActualMode);   // proves PR #27 base

        var interpText = ((StringValue)interp.Value!).Value;
        var compText = ((StringValue)comp.Value!).Value;
        Assert.Equal(fileContents, compText);
        Assert.Equal(interpText, compText);
    }

}
