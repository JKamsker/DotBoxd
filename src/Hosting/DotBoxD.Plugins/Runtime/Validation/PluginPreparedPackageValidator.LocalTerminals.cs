using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Plugins.Runtime;

using DotBoxD.Kernels;

internal static partial class PluginPreparedPackageValidator
{
    private static void ValidateLocalTerminalRouting(
        PluginPackage package,
        ExecutionPlan plan,
        List<SandboxDiagnostic> diagnostics)
    {
        ValidateLocalTerminalShape(package, diagnostics);
        if ((EntrypointEffects(package, plan) & SandboxEffect.HostStateWrite) == 0)
        {
            return;
        }

        foreach (var subscription in package.Manifest.Subscriptions)
        {
            if (subscription.LocalTerminal || subscription.ResultLocalTerminal)
            {
                diagnostics.Add(new SandboxDiagnostic(
                    "DBXK031",
                    "Local-terminal hook subscriptions must not declare host-write entrypoints."));
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
