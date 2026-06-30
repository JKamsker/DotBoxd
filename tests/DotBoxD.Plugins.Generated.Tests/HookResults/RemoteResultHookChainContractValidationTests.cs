using DotBoxD.Abstractions;
using DotBoxD.Kernels.Model;

namespace DotBoxD.Plugins.Generated.Tests.HookResults;

public sealed partial class RemoteResultHookChainTests
{
    [Fact]
    public void Local_result_install_rejects_manifest_result_type_that_does_not_match_hook_contract()
    {
        var package = WithSubscription(
            CaptureLocalPackage(),
            subscription => subscription with
            {
                ResultType = typeof(RemoteOtherDamageResult).FullName
            });
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        var pipeline = server.Hooks.On<RemoteDamageContext>();

        var exception = Assert.Throws<SandboxValidationException>(() =>
            pipeline.UseGeneratedLocalResultChain<RemoteOtherDamageResult>(
                package,
                (RemoteDamageContext _, HookContext _) => RemoteOtherDamageResult.Ok().WithDamage(1)));

        Assert.Contains(exception.Diagnostics, d =>
            d.Code == "DBXK033" &&
            d.Message.Contains(nameof(RemoteOtherDamageResult), StringComparison.Ordinal) &&
            d.Message.Contains(nameof(RemoteDamageResult), StringComparison.Ordinal));
    }
}
