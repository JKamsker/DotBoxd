using DotBoxD.Hosting;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests;

/// <summary>
/// Differential / parity tests for audit-success on the compiled side-effecting binding path.
/// PR #27 lifts the effects gate in BindingCallEmitter, allowing bindings declared with
/// CompiledRuntime.CallBinding stubs to run compiled even when they carry capability requirements,
/// external effects, or mandatory audit obligations.  Every test here runs the same module
/// interpreted AND compiled and asserts that every observable — result value, Succeeded,
/// ActualMode, sink deliveries, AuditEvent fields, ResourceUsage, Error.Code — is identical
/// between the two execution paths.  ActualMode==Compiled in the compiled run confirms that the
/// compiled path ran and did not silently fall back to the interpreter.
/// </summary>
public sealed class CompiledSideEffectAuditParityTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // host.message.send — PluginMessageBindings (CapabilityId = host.message.write)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Host_message_send_compiled_audit_fields_match_interpreted_field_for_field()
    {
        // Ensures ALL audit-event fields (Kind, BindingId, CapabilityId, Effect, ResourceId,
        // redacted Message, Fields["messageLength"]) are bit-for-bit identical between modes.
        const string moduleJson = """
        {
          "id": "parity-msg-fields",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "host.message.write" }],
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
                    "call": "host.message.send",
                    "args": [
                      { "string": "player-99" },
                      { "string": "token=s3cr3t hello world" }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;

        var (interp, interpSink) = await AuditParityMessageRunAsync(moduleJson, ExecutionMode.Interpreted);
        var (comp, compSink) = await AuditParityMessageRunAsync(moduleJson, ExecutionMode.Compiled);

        Assert.True(interp.Succeeded, interp.Error?.SafeMessage);
        Assert.True(comp.Succeeded, comp.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, interp.ActualMode);
        Assert.Equal(ExecutionMode.Compiled, comp.ActualMode);

        // Sink delivery — unredacted, identical in both modes.
        var im = Assert.Single(interpSink.Messages);
        var cm = Assert.Single(compSink.Messages);
        Assert.Equal(im.TargetId, cm.TargetId);
        Assert.Equal(im.Message, cm.Message);

        // Audit event: field-for-field parity.
        var ia = Assert.Single(interp.AuditEvents, e => e.Kind == "PluginMessage");
        var ca = Assert.Single(comp.AuditEvents, e => e.Kind == "PluginMessage");

        Assert.Equal(ia.Kind, ca.Kind);
        Assert.Equal(ia.BindingId, ca.BindingId);
        Assert.Equal(ia.CapabilityId, ca.CapabilityId);
        Assert.Equal(ia.Effect, ca.Effect);
        Assert.Equal(ia.ResourceId, ca.ResourceId);
        Assert.Equal(ia.Message, ca.Message);
        Assert.Equal(ia.Success, ca.Success);
        Assert.Equal(ia.Fields!["messageLength"], ca.Fields!["messageLength"]);
        Assert.Equal(ia.Fields["resourceKind"], ca.Fields["resourceKind"]);
    }

    [Fact]
    public async Task Host_message_send_compiled_ResourceUsage_HostCalls_matches_interpreted()
    {
        const string moduleJson = """
        {
          "id": "parity-msg-hostcalls",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "host.message.write" }],
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
                    "call": "host.message.send",
                    "args": [ { "string": "player-1" }, { "string": "hello" } ]
                  }
                }
              ]
            }
          ]
        }
        """;

        var (interp, _) = await AuditParityMessageRunAsync(moduleJson, ExecutionMode.Interpreted);
        var (comp, _) = await AuditParityMessageRunAsync(moduleJson, ExecutionMode.Compiled);

        Assert.True(interp.Succeeded, interp.Error?.SafeMessage);
        Assert.True(comp.Succeeded, comp.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, comp.ActualMode);
        Assert.Equal(interp.ResourceUsage.HostCalls, comp.ResourceUsage.HostCalls);
        Assert.Equal(1, comp.ResourceUsage.HostCalls);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // log.info — SafeLogBindings (CapabilityId = log.write)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Log_info_compiled_audit_fields_match_interpreted_field_for_field()
    {
        const string moduleJson = """
        {
          "id": "parity-log-info",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "log.write", "reason": "audit parity" }],
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
                    "call": "log.info",
                    "args": [{ "string": "hello parity" }]
                  }
                }
              ]
            }
          ]
        }
        """;

        var interp = await AuditParityLogRunAsync(moduleJson, ExecutionMode.Interpreted);
        var comp = await AuditParityLogRunAsync(moduleJson, ExecutionMode.Compiled);

        Assert.True(interp.Succeeded, interp.Error?.SafeMessage);
        Assert.True(comp.Succeeded, comp.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, interp.ActualMode);
        Assert.Equal(ExecutionMode.Compiled, comp.ActualMode);

        var ia = Assert.Single(interp.AuditEvents, e => e.Kind == "SandboxLog");
        var ca = Assert.Single(comp.AuditEvents, e => e.Kind == "SandboxLog");

        Assert.Equal(ia.Kind, ca.Kind);
        Assert.Equal(ia.BindingId, ca.BindingId);
        Assert.Equal(ia.CapabilityId, ca.CapabilityId);
        Assert.Equal(ia.Effect, ca.Effect);
        Assert.Equal(ia.ResourceId, ca.ResourceId);
        Assert.Equal(ia.Message, ca.Message);
        Assert.Equal(ia.Success, ca.Success);
        Assert.Equal(ia.Fields!["resourceKind"], ca.Fields!["resourceKind"]);

        // Concrete expected values pinned from the binding implementation.
        Assert.Equal("log.info", ca.BindingId);
        Assert.Equal("log.write", ca.CapabilityId);
        Assert.Equal("log:info", ca.ResourceId);
        Assert.Equal("hello parity", ca.Message);
        Assert.Equal("log", ca.Fields["resourceKind"]);
        Assert.Equal(1, comp.ResourceUsage.HostCalls);
        Assert.Equal(1, comp.ResourceUsage.LogEvents);
    }

    [Fact]
    public async Task Log_info_compiled_redacts_secrets_identically_to_interpreted()
    {
        const string moduleJson = """
        {
          "id": "parity-log-info-redact",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "log.write", "reason": "audit parity" }],
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
                    "call": "log.info",
                    "args": [{ "string": "token=abc123 ok" }]
                  }
                }
              ]
            }
          ]
        }
        """;

        var interp = await AuditParityLogRunAsync(moduleJson, ExecutionMode.Interpreted);
        var comp = await AuditParityLogRunAsync(moduleJson, ExecutionMode.Compiled);

        Assert.True(interp.Succeeded, interp.Error?.SafeMessage);
        Assert.True(comp.Succeeded, comp.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, comp.ActualMode);

        var ia = Assert.Single(interp.AuditEvents, e => e.Kind == "SandboxLog");
        var ca = Assert.Single(comp.AuditEvents, e => e.Kind == "SandboxLog");

        Assert.Equal(ia.Message, ca.Message);
        Assert.Equal("token=[redacted] ok", ca.Message);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // log.warn — SafeLogBindings (CapabilityId = log.write)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Log_warn_compiled_audit_fields_match_interpreted_field_for_field()
    {
        const string moduleJson = """
        {
          "id": "parity-log-warn",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "log.write", "reason": "audit parity" }],
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
                    "call": "log.warn",
                    "args": [{ "string": "careful" }]
                  }
                }
              ]
            }
          ]
        }
        """;

        var interp = await AuditParityLogRunAsync(moduleJson, ExecutionMode.Interpreted);
        var comp = await AuditParityLogRunAsync(moduleJson, ExecutionMode.Compiled);

        Assert.True(interp.Succeeded, interp.Error?.SafeMessage);
        Assert.True(comp.Succeeded, comp.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, interp.ActualMode);
        Assert.Equal(ExecutionMode.Compiled, comp.ActualMode);

        var ia = Assert.Single(interp.AuditEvents, e => e.Kind == "SandboxLog");
        var ca = Assert.Single(comp.AuditEvents, e => e.Kind == "SandboxLog");

        Assert.Equal(ia.Kind, ca.Kind);
        Assert.Equal(ia.BindingId, ca.BindingId);
        Assert.Equal(ia.CapabilityId, ca.CapabilityId);
        Assert.Equal(ia.Effect, ca.Effect);
        Assert.Equal(ia.ResourceId, ca.ResourceId);
        Assert.Equal(ia.Message, ca.Message);
        Assert.Equal(ia.Success, ca.Success);

        Assert.Equal("log.warn", ca.BindingId);
        Assert.Equal("log.write", ca.CapabilityId);
        Assert.Equal("log:warn", ca.ResourceId);
        Assert.Equal("careful", ca.Message);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // file.writeText — SafeFileBindings (CapabilityId = file.write)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task File_writeText_compiled_audit_fields_match_interpreted_field_for_field()
    {
        using var temp = AuditParityTempDirectory.Create();
        var existingPath = Path.Combine(temp.Path, "data.txt");
        await File.WriteAllTextAsync(existingPath, "old");

        const string moduleJson = """
        {
          "id": "parity-file-write",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "file.write" }],
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
                      { "path": "data.txt" },
                      { "string": "parity-content" }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;

        var interp = await AuditParityFileWriteRunAsync(temp.Path, moduleJson, ExecutionMode.Interpreted);
        await File.WriteAllTextAsync(existingPath, "old");  // reset for compiled run
        var comp = await AuditParityFileWriteRunAsync(temp.Path, moduleJson, ExecutionMode.Compiled);

        Assert.True(interp.Succeeded, interp.Error?.SafeMessage);
        Assert.True(comp.Succeeded, comp.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, interp.ActualMode);
        Assert.Equal(ExecutionMode.Compiled, comp.ActualMode);

        // File was written in both cases.
        Assert.Equal("parity-content", await File.ReadAllTextAsync(existingPath));

        // Audit field-for-field parity.
        var ia = Assert.Single(interp.AuditEvents, e => e.Kind == "BindingCall" && e.BindingId == "file.writeText");
        var ca = Assert.Single(comp.AuditEvents, e => e.Kind == "BindingCall" && e.BindingId == "file.writeText");

        Assert.Equal(ia.Kind, ca.Kind);
        Assert.Equal(ia.BindingId, ca.BindingId);
        Assert.Equal(ia.CapabilityId, ca.CapabilityId);
        Assert.Equal(ia.Effect, ca.Effect);
        Assert.Equal(ia.Success, ca.Success);
        Assert.Equal(ia.Bytes, ca.Bytes);
        Assert.Equal(ia.Fields!["resourceKind"], ca.Fields!["resourceKind"]);
        Assert.Equal("file", ca.Fields["resourceKind"]);
        Assert.Equal("file.writeText", ca.BindingId);
        Assert.Equal("file.write", ca.CapabilityId);
        Assert.Equal(SandboxEffect.FileWrite, ca.Effect);
        Assert.True(ca.Success);
    }

    [Fact]
    public async Task File_writeText_compiled_delivers_side_effect_and_increments_HostCalls()
    {
        using var temp = AuditParityTempDirectory.Create();
        var targetPath = Path.Combine(temp.Path, "written.txt");
        await File.WriteAllTextAsync(targetPath, "before");

        const string moduleJson = """
        {
          "id": "parity-file-write-delivery",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "file.write" }],
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
                      { "path": "written.txt" },
                      { "string": "compiled-wrote-this" }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;

        var comp = await AuditParityFileWriteRunAsync(temp.Path, moduleJson, ExecutionMode.Compiled);

        Assert.True(comp.Succeeded, comp.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, comp.ActualMode);
        Assert.Equal("compiled-wrote-this", await File.ReadAllTextAsync(targetPath));
        Assert.Equal(1, comp.ResourceUsage.HostCalls);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Two side-effecting calls in sequence: log.info + log.warn
    // Validates that two compiled side-effecting binding calls in the same
    // module both execute and both emit audit events.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Two_log_calls_compiled_each_emit_audit_event_matching_interpreted()
    {
        const string moduleJson = """
        {
          "id": "parity-two-logs",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "log.write", "reason": "dual call parity" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Unit",
              "body": [
                { "op": "expr", "value": { "call": "log.info", "args": [{ "string": "first" }] } },
                { "op": "return", "value": { "call": "log.warn", "args": [{ "string": "second" }] } }
              ]
            }
          ]
        }
        """;

        var interp = await AuditParityLogRunAsync(moduleJson, ExecutionMode.Interpreted);
        var comp = await AuditParityLogRunAsync(moduleJson, ExecutionMode.Compiled);

        Assert.True(interp.Succeeded, interp.Error?.SafeMessage);
        Assert.True(comp.Succeeded, comp.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, comp.ActualMode);

        // Both modes emit exactly 2 SandboxLog events.
        var iLogs = interp.AuditEvents.Where(e => e.Kind == "SandboxLog").ToList();
        var cLogs = comp.AuditEvents.Where(e => e.Kind == "SandboxLog").ToList();
        Assert.Equal(2, iLogs.Count);
        Assert.Equal(2, cLogs.Count);

        // Per-event field parity.
        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(iLogs[i].BindingId, cLogs[i].BindingId);
            Assert.Equal(iLogs[i].ResourceId, cLogs[i].ResourceId);
            Assert.Equal(iLogs[i].Message, cLogs[i].Message);
        }

        // ResourceUsage parity.
        Assert.Equal(interp.ResourceUsage.HostCalls, comp.ResourceUsage.HostCalls);
        Assert.Equal(interp.ResourceUsage.LogEvents, comp.ResourceUsage.LogEvents);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Host call quota is enforced identically for compiled side-effecting calls
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Log_info_quota_exceeded_error_code_matches_between_interpreted_and_compiled()
    {
        const string moduleJson = """
        {
          "id": "parity-log-quota",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "log.write", "reason": "quota parity" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Unit",
              "body": [
                { "op": "expr", "value": { "call": "log.info", "args": [{ "string": "first" }] } },
                { "op": "return", "value": { "call": "log.info", "args": [{ "string": "second" }] } }
              ]
            }
          ]
        }
        """;

        var interp = await AuditParityLogRunAsync(moduleJson, ExecutionMode.Interpreted, maxLogEvents: 1);
        var comp = await AuditParityLogRunAsync(moduleJson, ExecutionMode.Compiled, maxLogEvents: 1);

        Assert.False(interp.Succeeded);
        Assert.False(comp.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, interp.Error!.Code);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, comp.Error!.Code);
        Assert.Equal(interp.Error.Code, comp.Error.Code);
        Assert.Equal(ExecutionMode.Compiled, comp.ActualMode);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static async Task<(SandboxExecutionResult Result, InMemoryPluginMessageSink Sink)> AuditParityMessageRunAsync(
        string moduleJson,
        ExecutionMode mode)
    {
        var sink = new InMemoryPluginMessageSink();
        var host = Hosting.SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddPluginMessageBindings(sink);
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ImportJsonAsync(moduleJson);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite()
            .WithFuel(10_000)
            .Build());
        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });
        return (result, sink);
    }

    private static async Task<SandboxExecutionResult> AuditParityLogRunAsync(
        string moduleJson,
        ExecutionMode mode,
        int? maxLogEvents = null)
    {
        var host = Hosting.SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddLogBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ImportJsonAsync(moduleJson);
        var policyBuilder = SandboxPolicyBuilder.Create()
            .GrantLogging()
            .WithFuel(10_000);
        if (maxLogEvents.HasValue)
        {
            policyBuilder = policyBuilder.WithMaxLogEvents(maxLogEvents.Value);
        }

        var plan = await host.PrepareAsync(module, policyBuilder.Build());
        return await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });
    }

    private static async Task<SandboxExecutionResult> AuditParityFileWriteRunAsync(
        string root,
        string moduleJson,
        ExecutionMode mode)
    {
        var host = Hosting.SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddFileBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ImportJsonAsync(moduleJson);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create()
            .GrantFileWrite(root, 1024, allowCreate: false, allowOverwrite: true)
            .WithFuel(10_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build());
        return await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });
    }

    // Scoped temp-directory helper — defined locally so this file is self-contained
    // and cannot collide with helpers in other test files.
    private sealed class AuditParityTempDirectory : IDisposable
    {
        private AuditParityTempDirectory(string path) => Path = path;

        public string Path { get; }

        public static AuditParityTempDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dotboxd-parity-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new AuditParityTempDirectory(path);
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
