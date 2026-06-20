using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Plugins.Policies;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Kernels.Tests.Core.Regression;

/// <summary>
/// Regression coverage for COR-0057: the <c>host.message.write</c> grant must be
/// able to scope which recipients a plugin can message and how large a payload it
/// can send. The grant exposes typed <c>allowedTargets</c>, <c>targetPrefixes</c>,
/// and <c>maxMessageLength</c> parameters that are validated during policy
/// preparation and enforced (fail-closed) in the binding before the host sink is
/// called.
/// </summary>
public sealed class Fix_COR_0057_Tests
{
    [Fact]
    public async Task Allowed_target_in_recipient_set_is_delivered()
    {
        var messages = new InMemoryPluginMessageSink();
        var host = CreateHost(messages);
        var module = await host.ImportJsonAsync(SendModule("player-1", "hello"));
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite(allowedTargets: ["player-1", "player-2"])
            .WithFuel(10_000)
            .Build());

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal("player-1", Assert.Single(messages.Messages).TargetId);
    }

    [Fact]
    public async Task Target_outside_recipient_set_is_denied_before_sink_send()
    {
        var messages = new InMemoryPluginMessageSink();
        var host = CreateHost(messages);
        var module = await host.ImportJsonAsync(SendModule("player-999", "hello"));
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite(allowedTargets: ["player-1"])
            .WithFuel(10_000)
            .Build());

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PermissionDenied, result.Error!.Code);
        Assert.Empty(messages.Messages);
    }

    [Fact]
    public async Task Target_matching_granted_prefix_is_delivered()
    {
        var messages = new InMemoryPluginMessageSink();
        var host = CreateHost(messages);
        var module = await host.ImportJsonAsync(SendModule("team.red.player-1", "hello"));
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite(targetPrefixes: ["team.red."])
            .WithFuel(10_000)
            .Build());

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal("team.red.player-1", Assert.Single(messages.Messages).TargetId);
    }

    [Fact]
    public async Task Target_outside_granted_prefix_is_denied()
    {
        var messages = new InMemoryPluginMessageSink();
        var host = CreateHost(messages);
        var module = await host.ImportJsonAsync(SendModule("team.blue.player-1", "hello"));
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite(targetPrefixes: ["team.red."])
            .WithFuel(10_000)
            .Build());

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PermissionDenied, result.Error!.Code);
        Assert.Empty(messages.Messages);
    }

    [Fact]
    public async Task Message_exceeding_granted_length_limit_is_denied()
    {
        var messages = new InMemoryPluginMessageSink();
        var host = CreateHost(messages);
        var module = await host.ImportJsonAsync(SendModule("player-1", "0123456789"));
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite(maxMessageLength: 4)
            .WithFuel(10_000)
            .Build());

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        Assert.Empty(messages.Messages);
    }

    [Fact]
    public async Task Message_within_granted_length_limit_is_delivered()
    {
        var messages = new InMemoryPluginMessageSink();
        var host = CreateHost(messages);
        var module = await host.ImportJsonAsync(SendModule("player-1", "1234"));
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite(maxMessageLength: 4)
            .WithFuel(10_000)
            .Build());

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal("1234", Assert.Single(messages.Messages).Message);
    }

    [Fact]
    public async Task Unrestricted_grant_still_allows_any_target()
    {
        var messages = new InMemoryPluginMessageSink();
        var host = CreateHost(messages);
        var module = await host.ImportJsonAsync(SendModule("anything", "hello"));
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite()
            .WithFuel(10_000)
            .Build());

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal("anything", Assert.Single(messages.Messages).TargetId);
    }

    [Theory]
    [MemberData(nameof(EmptyRecipientScopes))]
    public void Empty_allowed_target_scope_is_rejected(string[] allowedTargets)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            SandboxPolicyBuilder.Create().GrantHostMessageWrite(allowedTargets: allowedTargets));

        Assert.Equal("allowedTargets", ex.ParamName);
    }

    [Theory]
    [MemberData(nameof(EmptyRecipientScopes))]
    public void Empty_target_prefix_scope_is_rejected(string[] targetPrefixes)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            SandboxPolicyBuilder.Create().GrantHostMessageWrite(targetPrefixes: targetPrefixes));

        Assert.Equal("targetPrefixes", ex.ParamName);
    }

    [Fact]
    public async Task Unsupported_grant_parameter_is_rejected_during_preparation()
    {
        var messages = new InMemoryPluginMessageSink();
        var host = CreateHost(messages);
        var module = await host.ImportJsonAsync(SendModule("player-1", "hello"));
        var policy = SandboxPolicyBuilder.Create()
            .Grant("host.message.write", new { unsupportedKey = "value" })
            .WithFuel(10_000)
            .Build();

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await host.PrepareAsync(module, policy));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-POLICY-GRANT-PARAM");
    }

    [Fact]
    public async Task Unsupported_wildcard_grant_parameter_is_rejected_during_preparation()
    {
        var messages = new InMemoryPluginMessageSink();
        var host = CreateHost(messages);
        var module = await host.ImportJsonAsync(SendModule("player-1", "hello"));
        var policy = SandboxPolicyBuilder.Create()
            .AllowRuntimeAsync()
            .Grant(
                "host.*",
                new { allowedTarget = "player-1" },
                SandboxEffect.HostStateWrite | SandboxEffect.Audit)
            .WithFuel(10_000)
            .Build();

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await host.PrepareAsync(module, policy));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-POLICY-GRANT-PARAM");
    }

    [Fact]
    public async Task Invalid_max_message_length_grant_is_rejected_during_preparation()
    {
        var messages = new InMemoryPluginMessageSink();
        var host = CreateHost(messages);
        var module = await host.ImportJsonAsync(SendModule("player-1", "hello"));
        var policy = SandboxPolicyBuilder.Create()
            .Grant("host.message.write", new { maxMessageLength = "not-a-number" })
            .WithFuel(10_000)
            .Build();

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await host.PrepareAsync(module, policy));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-POLICY-GRANT-PARAM");
    }

    public static TheoryData<string[]> EmptyRecipientScopes()
        => new()
        {
            Array.Empty<string>(),
            new[] { "" },
            new[] { " ", null! }
        };

    private static SandboxHost CreateHost(IPluginMessageSink sink)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddPluginMessageBindings(sink);
            builder.UseInterpreter();
        });

    private static string SendModule(string targetId, string message) =>
        $$"""
        {
          "id": "cor-0057-message",
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
                      { "string": "{{targetId}}" },
                      { "string": "{{message}}" }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;
}
