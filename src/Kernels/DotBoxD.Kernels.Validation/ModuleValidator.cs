using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Validation.Model;

namespace DotBoxD.Kernels.Validation;

using DotBoxD.Kernels;

public sealed class ModuleValidator
{
    private static readonly IReadOnlySet<string> NoDeclaredOpaqueIdTypes =
        new HashSet<string>(StringComparer.Ordinal);

    public ModuleValidationResult Validate(SandboxModule module, IBindingCatalog bindings, SandboxPolicy? policy = null)
    {
        var diagnostics = new List<SandboxDiagnostic>();
        var declaredOpaqueIdTypes = policy?.DeclaredOpaqueIdTypes ?? NoDeclaredOpaqueIdTypes;
        StructuralValidator.Validate(module, diagnostics, declaredOpaqueIdTypes);
        if (diagnostics.Count > 0) {
            return ModuleValidationResult.Failure(diagnostics);
        }

        IReadOnlyDictionary<string, FunctionAnalysis> functions;
        IReadOnlyDictionary<string, IReadOnlySet<string>> bindingReferences;
        IReadOnlySet<string> requiredCapabilities;
        SandboxEffect requiredEffects;
        try
        {
            var analyzer = new FunctionAnalyzer(module, bindings, diagnostics, declaredOpaqueIdTypes);
            functions = analyzer.AnalyzeAll();
            requiredEffects = RequiredEffects(module, functions);
            bindingReferences = BindingReferenceCollector.CollectByFunction(module, bindings);
            requiredCapabilities = RequiredCapabilities(module, bindings, bindingReferences);
            PolicyResolver.Validate(module, bindings, policy, requiredEffects, requiredCapabilities, diagnostics);
        }
        catch (SandboxValidationException ex)
        {
            diagnostics.AddRange(ex.Diagnostics);
            return ModuleValidationResult.Failure(diagnostics);
        }

        return new ModuleValidationResult(
            HasNoErrors(diagnostics),
            diagnostics,
            functions,
            requiredEffects,
            requiredCapabilities,
            bindingReferences);
    }

    private static SandboxEffect RequiredEffects(
        SandboxModule module,
        IReadOnlyDictionary<string, FunctionAnalysis> functions)
    {
        var effects = SandboxEffect.None;
        foreach (var function in module.Functions)
        {
            if (function.IsEntrypoint)
            {
                effects |= functions[function.Id].Effects;
            }
        }

        return effects;
    }

    private static IReadOnlySet<string> RequiredCapabilities(
        SandboxModule module,
        IBindingCatalog bindings,
        IReadOnlyDictionary<string, IReadOnlySet<string>> bindingReferences)
    {
        var required = new HashSet<string>(StringComparer.Ordinal);
        foreach (var function in module.Functions)
        {
            if (!function.IsEntrypoint)
            {
                continue;
            }

            if (!bindingReferences.TryGetValue(function.Id, out var references)) {
                continue;
            }

            foreach (var bindingId in references)
            {
                if (!bindings.TryGet(bindingId, out var binding))
                {
                    continue;
                }

                if (binding.RequiredCapability is not null)
                {
                    required.Add(binding.RequiredCapability);
                }

                if (RequiresRuntimeAsync(binding))
                {
                    required.Add(RuntimeCapabilityIds.Async);
                }
            }
        }

        return required;
    }

    private static bool RequiresRuntimeAsync(BindingSignature binding)
        => binding.IsAsync || (binding.Effects & SandboxEffect.Concurrency) != 0;

    private static bool HasNoErrors(IReadOnlyList<SandboxDiagnostic> diagnostics)
    {
        for (var i = 0; i < diagnostics.Count; i++)
        {
            if (diagnostics[i].Severity == DiagnosticSeverity.Error)
            {
                return false;
            }
        }

        return true;
    }
}
