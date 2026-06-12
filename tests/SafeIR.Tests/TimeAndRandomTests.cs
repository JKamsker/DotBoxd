using SafeIR;

namespace SafeIR.Tests;

public sealed class TimeAndRandomTests
{
    [Fact]
    public async Task Time_binding_requires_host_grant()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync(TimeJson());

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-POLICY-CAP");
    }

    [Fact]
    public async Task Deterministic_time_uses_policy_logical_clock()
    {
        var host = SandboxTestHost.Create();
        var logicalNow = DateTimeOffset.Parse("2026-06-11T10:15:30Z");
        var module = await host.ParseJsonAsync(TimeJson());
        var policy = SandboxPolicyBuilder.Create()
            .GrantTimeNow()
            .Deterministic(logicalNow, randomSeed: 1)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded);
        Assert.Equal(logicalNow.ToUnixTimeMilliseconds(), ((I64Value)result.Value!).Value);
        Assert.Contains(result.AuditEvents, e => e.BindingId == "time.nowUnixMillis" && e.Success);
    }

    [Fact]
    public async Task Deterministic_random_replays_from_policy_seed()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync(RandomSumJson());
        var policy = SandboxPolicyBuilder.Create()
            .GrantRandom()
            .Deterministic(DateTimeOffset.UnixEpoch, randomSeed: 123)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var first = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);
        var second = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(((I32Value)first.Value!).Value, ((I32Value)second.Value!).Value);
    }

    [Fact]
    public async Task Deterministic_random_uses_full_ulong_seed()
    {
        var first = await ExecuteRandomSumAsync(0x0000_0000_0000_0001UL);
        var second = await ExecuteRandomSumAsync(0x0000_0001_0000_0001UL);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task Deterministic_random_policy_requires_seed()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync(RandomSumJson());
        var policy = new SandboxPolicy(
            "deterministic-without-seed",
            SandboxEffects.Pure | SandboxEffect.Random,
            [new CapabilityGrant("random", new Dictionary<string, string>())],
            new ResourceLimits(),
            Deterministic: true,
            LogicalNow: DateTimeOffset.UnixEpoch,
            RandomSeed: null);

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, policy));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-POLICY-DETERMINISM");
    }

    private static string TimeJson()
        => """
        {
          "id": "clock",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "time.now" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I64",
              "body": [
                { "op": "return", "value": { "call": "time.nowUnixMillis", "args": [] } }
              ]
            }
          ]
        }
        """;

    private static async Task<int> ExecuteRandomSumAsync(ulong seed)
    {
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync(RandomSumJson());
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .GrantRandom()
                .Deterministic(DateTimeOffset.UnixEpoch, seed)
                .Build());
        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);
        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        return ((I32Value)result.Value!).Value;
    }

    private static string RandomSumJson()
        => """
        {
          "id": "random-sum",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "random" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                {
                  "op": "set",
                  "name": "first",
                  "value": {
                    "call": "random.nextI32",
                    "args": [{ "i32": 0 }, { "i32": 1000 }]
                  }
                },
                {
                  "op": "set",
                  "name": "second",
                  "value": {
                    "call": "random.nextI32",
                    "args": [{ "i32": 0 }, { "i32": 1000 }]
                  }
                },
                {
                  "op": "return",
                  "value": { "op": "add", "left": { "var": "first" }, "right": { "var": "second" } }
                }
              ]
            }
          ]
        }
        """;
}
