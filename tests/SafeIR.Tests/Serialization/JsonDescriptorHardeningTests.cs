using SafeIR.Hosting;

namespace SafeIR.Tests;

public sealed class JsonDescriptorHardeningTests
{
    [Theory]
    [InlineData("Acme.Plugin.FireKernel, Acme.Plugin, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null")]
    [InlineData("RuntimeMethodHandle")]
    [InlineData("MetadataToken=06000001")]
    [InlineData("rawIlBase64")]
    public async Task Metadata_rejects_loader_and_il_descriptors(string metadataValue)
    {
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
        });
        var module = await host.ImportJsonAsync(MinimalModule(
            $$"""
            "metadata": { "debug": "{{metadataValue}}" },
            """));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-IR-CLR-REF");
    }

    [Fact]
    public void Metadata_token_detection_requires_boundaries_around_prefixed_tokens()
    {
        Assert.True(SandboxDescriptorGuards.ContainsForbiddenDescriptor("token 0x02000001"));
        Assert.False(SandboxDescriptorGuards.ContainsForbiddenDescriptor("abc0x02000001def"));
    }

    [Fact]
    public async Task Duplicate_capability_requests_are_rejected()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync("""
        {
          "id": "duplicate-capability",
          "version": "1.0.0",
          "capabilityRequests": [
            { "id": "log.write" },
            { "id": "log.write", "reason": "again" }
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

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-STRUCT-DUP-CAP");
    }

    [Fact]
    public async Task Duplicate_function_ids_are_rejected()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync("""
        {
          "id": "duplicate-function",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "i32": 1 } }]
            },
            {
              "id": "main",
              "visibility": "private",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "i32": 2 } }]
            }
          ]
        }
        """);

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-STRUCT-DUP-FN");
    }

    private static string MinimalModule(string extraModuleProperty, string returnValue = """{ "i32": 1 }""")
        => $$"""
        {
          "id": "schema-check",
          "version": "1.0.0",
          {{extraModuleProperty}}
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": {{returnValue}} }]
            }
          ]
        }
        """;
}
