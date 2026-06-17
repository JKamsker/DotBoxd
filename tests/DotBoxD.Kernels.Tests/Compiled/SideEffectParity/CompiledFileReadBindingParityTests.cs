using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

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
        using var temp = FileReadParityTempDirectory.Create();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "read-me.txt"), fileContents);

        // Act
        var interp = await FileReadParityRunAsync(temp.Path, "read-me.txt", ExecutionMode.Interpreted);
        var comp = await FileReadParityRunAsync(temp.Path, "read-me.txt", ExecutionMode.Compiled);

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
        using var temp = FileReadParityTempDirectory.Create();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "data.txt"), fileContents);

        // Act
        var interp = await FileReadParityRunAsync(temp.Path, "data.txt", ExecutionMode.Interpreted);
        var comp = await FileReadParityRunAsync(temp.Path, "data.txt", ExecutionMode.Compiled);

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
        using var temp = FileReadParityTempDirectory.Create();
        // Default UTF-8 (no BOM) so the on-disk byte count equals Encoding.UTF8.GetByteCount(contents).
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "measure.txt"), fileContents);

        var interp = await FileReadParityRunAsync(temp.Path, "measure.txt", ExecutionMode.Interpreted);
        var comp = await FileReadParityRunAsync(temp.Path, "measure.txt", ExecutionMode.Compiled);

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
        using var temp = FileReadParityTempDirectory.Create();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "hostcalls.txt"), "hostcalls-content");

        var interp = await FileReadParityRunAsync(temp.Path, "hostcalls.txt", ExecutionMode.Interpreted);
        var comp = await FileReadParityRunAsync(temp.Path, "hostcalls.txt", ExecutionMode.Compiled);

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
        using var temp = FileReadParityTempDirectory.Create();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "bytes.txt"), fileContents);

        var interp = await FileReadParityRunAsync(temp.Path, "bytes.txt", ExecutionMode.Interpreted);
        var comp = await FileReadParityRunAsync(temp.Path, "bytes.txt", ExecutionMode.Compiled);

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
        using var temp = FileReadParityTempDirectory.Create();
        // Default UTF-8 (no BOM) so the read-back equals the original string exactly.
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "unicode.txt"), fileContents);

        var interp = await FileReadParityRunAsync(temp.Path, "unicode.txt", ExecutionMode.Interpreted);
        var comp = await FileReadParityRunAsync(temp.Path, "unicode.txt", ExecutionMode.Compiled);

        Assert.True(interp.Succeeded, interp.Error?.SafeMessage);
        Assert.True(comp.Succeeded, comp.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, comp.ActualMode);   // proves PR #27 base

        var interpText = ((StringValue)interp.Value!).Value;
        var compText = ((StringValue)comp.Value!).Value;
        Assert.Equal(fileContents, compText);
        Assert.Equal(interpText, compText);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Capability denial: file.read not granted — validated at PrepareAsync
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FileReadText_without_capability_grant_throws_SandboxValidationException_at_prepare()
    {
        // Arrange — no file.read capability in policy; the module requests it so validation fails
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddFileBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

        const string moduleJson = """
        {
          "id": "parity-file-read-nocap",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "file.read", "reason": "test read" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "String",
              "body": [
                {
                  "op": "return",
                  "value": {
                    "call": "file.readText",
                    "args": [{ "path": "cap.txt" }]
                  }
                }
              ]
            }
          ]
        }
        """;

        var module = await host.ImportJsonAsync(moduleJson);

        // No GrantFileRead — policy-level denial surfaces at PrepareAsync in both modes
        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build()));
        Assert.Contains(ex.Diagnostics, d => d.Code == "E-POLICY-CAP");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Error path: file does not exist
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FileReadText_missing_file_error_code_matches_between_interpreted_and_compiled()
    {
        // Arrange — grant the directory root but do NOT create the file
        using var temp = FileReadParityTempDirectory.Create();

        // Act
        var interp = await FileReadParityRunAsync(temp.Path, "missing.txt", ExecutionMode.Interpreted);
        var comp = await FileReadParityRunAsync(temp.Path, "missing.txt", ExecutionMode.Compiled);

        // Assert — both fail; compiled must actually run the binding (ActualMode==Compiled)
        Assert.False(interp.Succeeded);
        Assert.False(comp.Succeeded);
        // Interpreted: file not found at runtime → NotFound
        Assert.Equal(SandboxErrorCode.NotFound, interp.Error!.Code);
        // Compiled (PR #27 base): same binding path → same NotFound
        Assert.Equal(SandboxErrorCode.NotFound, comp.Error!.Code);
        Assert.Equal(interp.Error.Code, comp.Error.Code);
        Assert.Equal(ExecutionMode.Compiled, comp.ActualMode);   // proves PR #27 base
    }

    [Fact]
    public async Task FileReadText_missing_file_failure_audit_event_matches_between_modes()
    {
        // Arrange — grant root but don't create the file
        using var temp = FileReadParityTempDirectory.Create();

        // Act
        var interp = await FileReadParityRunAsync(temp.Path, "gone.txt", ExecutionMode.Interpreted);
        var comp = await FileReadParityRunAsync(temp.Path, "gone.txt", ExecutionMode.Compiled);

        Assert.False(interp.Succeeded);
        Assert.False(comp.Succeeded);
        Assert.Equal(ExecutionMode.Compiled, comp.ActualMode);   // proves PR #27 base

        // Both modes emit a failure BindingCall event for file.readText
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

    // ──────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static async Task<SandboxExecutionResult> FileReadParityRunAsync(
        string root,
        string relativePath,
        ExecutionMode mode)
    {
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddFileBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

        // Module shape mirrors InterpreterAndPolicyTests.FileReadJson
        var moduleJson = $$"""
        {
          "id": "parity-file-read",
          "version": "1.0.0",
          "capabilityRequests": [
            { "id": "file.read", "reason": "parity test read" }
          ],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "String",
              "body": [
                {
                  "op": "return",
                  "value": {
                    "call": "file.readText",
                    "args": [{ "path": "{{relativePath.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}}" }]
                  }
                }
              ]
            }
          ]
        }
        """;

        var module = await host.ImportJsonAsync(moduleJson);

        // GrantFileRead scoped to the temp directory root
        var policy = SandboxPolicyBuilder.Create()
            .AllowRuntimeAsync()
            .GrantFileRead(root, maxBytesPerRun: 65_536)
            .WithFuel(10_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();

        var plan = await host.PrepareAsync(module, policy);

        return await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });
    }

    // Scoped temp-directory helper — defined locally to avoid collisions with helpers
    // in other test files when all files are compiled together.
    private sealed class FileReadParityTempDirectory : IDisposable
    {
        private FileReadParityTempDirectory(string path) => Path = path;

        public string Path { get; }

        public static FileReadParityTempDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "dotboxd-fileread-parity-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new FileReadParityTempDirectory(path);
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
