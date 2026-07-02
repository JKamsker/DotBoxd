using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Policy;

public sealed partial class PolicyBoundaryTests
{
    [Fact]
    public async Task Unknown_capability_request_cannot_be_satisfied_by_unknown_wildcard_grant()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(UnknownCapabilityRequestJson());
        var policy = new SandboxPolicy(
            "unknown-wildcard-request",
            SandboxEffects.Pure,
            [new CapabilityGrant("vendor.*", new Dictionary<string, string>())],
            new ResourceLimits(MaxFuel: 5_000));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, policy));

        Assert.Contains(
            ex.Diagnostics,
            d => d.Code == "E-POLICY-GRANT" &&
                 d.Message.Contains("vendor.*", StringComparison.Ordinal) &&
                 d.Message.Contains("vendor.secret", StringComparison.Ordinal));
    }
}
