using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Plugins.Runtime;

using DotBoxD.Kernels;

internal static partial class PluginPreparedPackageValidator
{
    private static void ValidateLocalTerminalEffects(
        IReadOnlyList<HookSubscriptionManifest> subscriptions,
        ExecutionPlan plan,
        string handleId,
        List<SandboxDiagnostic> diagnostics)
    {
        if (!plan.FunctionAnalysis.TryGetValue(handleId, out var handleAnalysis) ||
            (handleAnalysis.Effects & SandboxEffect.HostStateWrite) == 0)
        {
            return;
        }

        foreach (var subscription in subscriptions)
        {
            if (subscription.LocalTerminal || subscription.ResultLocalTerminal)
            {
                diagnostics.Add(new SandboxDiagnostic(
                    "DBXK031",
                    "Local-terminal hook subscriptions must not declare a host-write Handle entrypoint."));
                return;
            }
        }
    }
}
