using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed partial class RemoteRunLocalValidationTests
{
    [Fact]
    public async Task Local_terminal_package_without_callback_subscription_id_is_rejected_at_install()
    {
        var package = LowerToPackage(ScalarProjectionSource);
        var subscription = Assert.Single(package.Manifest.Subscriptions);
        var metadata = package.Module.Metadata
            .Where(pair => !string.Equals(pair.Key, "callbackSubscriptionId", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var tampered = package with { Module = package.Module with { Metadata = metadata } };

        Assert.True(subscription.LocalTerminal);
        Assert.Null(tampered.CallbackSubscriptionId);

        using var server = PluginServer.Create(new InMemoryPluginMessageSink(), defaultPolicy: Policy());
        var ex = await Assert.ThrowsAsync<DotBoxD.Kernels.Model.SandboxValidationException>(
            async () => await server.InstallAsync(tampered).AsTask());
        Assert.Contains(ex.Diagnostics, d =>
            d.Code == "DBXK031" &&
            d.Message.Contains("callbackSubscriptionId", StringComparison.Ordinal));
    }
}
