using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

public sealed class RemoteHookPipelineResultValidationTests
{
    [Fact]
    public void UseGeneratedResultChain_reports_missing_hook_result_metadata_without_null_deref()
    {
        var registry = new RemoteHookRegistry(_ => throw new InvalidOperationException("Install should not run."));
        var package = PackageFor<NoHookResultMetadataEvent>();

        var exception = Assert.Throws<InvalidOperationException>(
            () => registry.On<NoHookResultMetadataEvent>().UseGeneratedResultChain<RemoteValidationResult>(package));

        Assert.Contains("with result type", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'<none>'", exception.Message, StringComparison.Ordinal);
    }

    private static PluginPackage PackageFor<TEvent>()
    {
        var package = FireDamagePluginPackage.Create();
        return package with
        {
            Manifest = package.Manifest with
            {
                Subscriptions =
                [
                    new HookSubscriptionManifest(typeof(TEvent).FullName!, "FireDamageKernel")
                    {
                        ResultType = typeof(RemoteValidationResult).FullName
                    }
                ]
            }
        };
    }

    private sealed record NoHookResultMetadataEvent(string Id);

    private readonly record struct RemoteValidationResult(bool Success, string? Reason) : IHookResult;
}
