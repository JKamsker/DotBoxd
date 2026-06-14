using DotBoxd.Hosting;
using DotBoxd.Hosting.Http;

namespace DotBoxd.Kernels.Tests;

public sealed class HttpApiSurfaceTests
{
    [Fact]
    public void Http_setup_helpers_are_available_from_public_namespace()
    {
        using var host = SandboxHost.Create(builder => builder.AddNetworkBindings());
        var registry = new BindingRegistryBuilder().AddNetworkBindings().Build();
        var policy = SandboxPolicyBuilder.Create()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024)
            .Build();

        Assert.NotNull(host);
        Assert.True(registry.TryGet("net.http.get", out _));
        Assert.True(policy.GrantsCapability("net.http.get"));
    }
}
