using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Runtime.File;

public sealed class SafeFileAuditRedactionTests
{
    [Fact]
    public async Task File_read_failure_audit_redacts_invalid_programmatic_path()
    {
        using var temp = TempDirectory.Create();
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync("""
        {
          "id": "file-path-input",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "file.read" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "path", "type": "SandboxPath" }],
              "returnType": "String",
              "body": [
                {
                  "op": "return",
                  "value": { "call": "file.readText", "args": [{ "var": "path" }] }
                }
              ]
            }
          ]
        }
        """);
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .AllowRuntimeAsync()
                .GrantFileRead(temp.Path, 1024)
                .WithFuel(5_000)
                .Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            new SandboxPathValue(new SandboxPath("../secret.txt")));

        // A malformed (non-portable, traversal) SandboxPath input is now rejected at the
        // entrypoint value boundary (SandboxValueValidator.RequireScalarInvariants) before the
        // file.readText binding runs, so the failure is InvalidInput. Regardless of where it is
        // rejected, no audit event may echo the traversal path or its secret-shaped token.
        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
        Assert.All(result.AuditEvents, e =>
        {
            Assert.DoesNotContain("secret", e.ResourceId ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret", e.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("..", e.ResourceId ?? string.Empty, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task File_read_failure_audit_redacts_secret_shaped_valid_path()
    {
        using var temp = TempDirectory.Create();
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync("""
        {
          "id": "file-secret-path-input",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "file.read" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "path", "type": "SandboxPath" }],
              "returnType": "String",
              "body": [
                {
                  "op": "return",
                  "value": { "call": "file.readText", "args": [{ "var": "path" }] }
                }
              ]
            }
          ]
        }
        """);
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .AllowRuntimeAsync()
                .GrantFileRead(temp.Path, 1024)
                .WithFuel(5_000)
                .Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            new SandboxPathValue(new SandboxPath("profiles/token-abc123.json")));

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.NotFound, result.Error!.Code);
        var audit = Assert.Single(result.AuditEvents, e => e.BindingId == "file.readText" && !e.Success);
        Assert.DoesNotContain("abc123", audit.ResourceId, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", audit.ResourceId, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "dotboxd-file-audit-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
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
