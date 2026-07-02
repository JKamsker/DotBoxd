using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Policy;

public sealed class PolicyEventGrantRegressionTests
{
    [Fact]
    public async Task Prepare_rejects_unrequired_event_read_grant()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var policy = new SandboxPolicy(
            "unrequired-event-read",
            SandboxEffects.Pure,
            [new CapabilityGrant("event.read.secret", new Dictionary<string, string>())],
            new ResourceLimits(MaxFuel: 5_000));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, policy));

        Assert.Contains(
            ex.Diagnostics,
            diagnostic =>
                diagnostic.Code == "E-POLICY-GRANT" &&
                diagnostic.Message.Contains("event.read.secret", StringComparison.Ordinal));
    }
}
