using DotBoxd.Hosting;

namespace DotBoxd.Kernels.Tests;

public sealed class IrIdentifierValidationTests
{
    [Fact]
    public async Task Module_rejects_empty_identifier()
    {
        var module = await Host.ImportJsonAsync("""
        {
          "id": "",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "i32": 1 } }]
            }
          ]
        }
        """);

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await Host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-IR-ID");
    }

    [Fact]
    public async Task Function_rejects_control_character_identifier()
    {
        var module = await Host.ImportJsonAsync("""
        {
          "id": "bad-function",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main\u0001",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "i32": 1 } }]
            }
          ]
        }
        """);

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await Host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-IR-ID");
    }

    [Fact]
    public async Task Statement_rejects_empty_local_name()
    {
        var module = await Host.ImportJsonAsync("""
        {
          "id": "bad-local",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                { "op": "set", "name": "", "value": { "i32": 1 } },
                { "op": "return", "value": { "i32": 1 } }
              ]
            }
          ]
        }
        """);

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await Host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-IR-ID");
    }

    [Fact]
    public async Task Capability_reason_allows_human_text()
    {
        var module = await Host.ImportJsonAsync("""
        {
          "id": "reason-text",
          "version": "1.0.0",
          "capabilityRequests": [
            { "id": "file.read", "reason": "Read tenant-local config" }
          ],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "i32": 1 } }]
            }
          ]
        }
        """);

        var policy = SandboxPolicyBuilder.Create().GrantFileRead(Path.GetTempPath(), 1).Build();
        var plan = await Host.PrepareAsync(module, policy);

        Assert.Equal("reason-text", plan.Module.Id);
    }

    private static SandboxHost Host => SandboxHost.Create(builder => {
        builder.AddDefaultPureBindings();
        builder.UseInterpreter();
    });
}
