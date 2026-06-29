using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Plugins.Runtime;

using DotBoxD.Kernels;

internal static partial class PluginPreparedPackageValidator
{
    private static void ValidateLocalTerminalRouting(
        PluginPackage package,
        ExecutionPlan plan,
        string handleId,
        List<SandboxDiagnostic> diagnostics)
    {
        ValidateLocalTerminalShape(package, diagnostics);
        if (!plan.FunctionAnalysis.TryGetValue(handleId, out var handleAnalysis) ||
            (handleAnalysis.Effects & SandboxEffect.HostStateWrite) == 0)
        {
            return;
        }

        foreach (var subscription in package.Manifest.Subscriptions)
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

    private static void ValidateLocalTerminalShape(PluginPackage package, List<SandboxDiagnostic> diagnostics)
    {
        foreach (var subscription in package.Manifest.Subscriptions)
        {
            if ((subscription.LocalTerminal || subscription.ResultLocalTerminal) &&
                package.CallbackSubscriptionId is null)
            {
                diagnostics.Add(new SandboxDiagnostic(
                    "DBXK031",
                    "Local-terminal hook subscriptions must declare callbackSubscriptionId metadata."));
                return;
            }

            if (subscription.LocalTerminal && subscription.ProjectedType is null)
            {
                diagnostics.Add(new SandboxDiagnostic(
                    "DBXK031",
                    "Local-terminal hook subscriptions must declare an explicit projected type."));
                return;
            }
        }
    }
}
