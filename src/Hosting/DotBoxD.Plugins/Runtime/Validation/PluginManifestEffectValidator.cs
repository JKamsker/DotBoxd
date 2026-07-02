using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Plugins.Runtime;

internal static class PluginManifestEffectValidator
{
    public static SandboxEffect Validate(
        PluginManifest manifest,
        List<SandboxDiagnostic> diagnostics)
    {
        var effects = SandboxEffect.None;
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var effect in manifest.Effects)
        {
            if (!TryParseDeclaredEffect(effect, out var parsed))
            {
                diagnostics.Add(new SandboxDiagnostic("DBXK040", $"Plugin manifest effect '{effect}' is not supported."));
                continue;
            }

            if (!declared.Add(effect))
            {
                diagnostics.Add(new SandboxDiagnostic(
                    "DBXK040",
                    $"Plugin manifest effect '{effect}' is declared more than once."));
            }

            effects |= parsed;
        }

        if (effects == SandboxEffect.None)
        {
            diagnostics.Add(new SandboxDiagnostic("DBXK040", "Plugin manifest must declare verified effects."));
        }

        return effects;
    }

    private static bool TryParseDeclaredEffect(
        string effect,
        out SandboxEffect parsed)
    {
        parsed = effect switch
        {
            nameof(SandboxEffect.Cpu) => SandboxEffect.Cpu,
            nameof(SandboxEffect.Alloc) => SandboxEffect.Alloc,
            nameof(SandboxEffect.Time) => SandboxEffect.Time,
            nameof(SandboxEffect.Random) => SandboxEffect.Random,
            nameof(SandboxEffect.FileRead) => SandboxEffect.FileRead,
            nameof(SandboxEffect.FileWrite) => SandboxEffect.FileWrite,
            nameof(SandboxEffect.Network) => SandboxEffect.Network,
            nameof(SandboxEffect.HostStateRead) => SandboxEffect.HostStateRead,
            nameof(SandboxEffect.HostStateWrite) => SandboxEffect.HostStateWrite,
            nameof(SandboxEffect.Concurrency) => SandboxEffect.Concurrency,
            nameof(SandboxEffect.Audit) => SandboxEffect.Audit,
            _ => SandboxEffect.None
        };
        return parsed != SandboxEffect.None;
    }
}
